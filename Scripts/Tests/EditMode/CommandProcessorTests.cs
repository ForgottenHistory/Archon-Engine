using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Core;
using Core.Commands;
using Core.Network;

namespace Tests.EditMode
{
    /// <summary>
    /// Tests for CommandProcessor - command registration, network routing and the wire
    /// format.
    ///
    /// WHY THIS MATTERS: command type IDs are assigned by registration order and sent
    /// over the wire as bare integers. If two clients register in different orders, they
    /// decode each other's commands as the WRONG TYPE - and it will not throw, it will
    /// quietly execute the wrong command. Nothing enforces matching order today; these
    /// tests at least pin the ID assignment rule.
    ///
    /// SCOPE LIMIT: GameState is a MonoBehaviour and every execution path dereferences
    /// gameState.EventBus, so command EXECUTION cannot be covered from EditMode. These
    /// tests deliberately stay on paths that stop before execution - validation
    /// rejection, client-side routing, and unregistered-type handling. Execution,
    /// host-side broadcast and remote-command handling need a PlayMode test with a real
    /// GameState.
    /// </summary>
    public class CommandProcessorTests
    {
        // ===== Test doubles =====

        /// <summary>
        /// Command whose Validate always fails, so SubmitCommand returns before touching
        /// gameState. Records whether it was serialized.
        /// </summary>
        private class RejectingCommand : BaseCommand
        {
            public int Payload;
            public int SerializeCallCount;

            public override bool Validate(GameState gameState) => false;

            public override void Execute(GameState gameState) => throw new InvalidOperationException(
                "Execute must not be reached when Validate returns false");

            public override void Undo(GameState gameState) { }

            public override void Serialize(BinaryWriter writer)
            {
                SerializeCallCount++;
                writer.Write(Payload);
            }

            public override void Deserialize(BinaryReader reader) => Payload = reader.ReadInt32();
        }

        private class SecondRejectingCommand : RejectingCommand { }
        private class ThirdRejectingCommand : RejectingCommand { }

        /// <summary>
        /// Command that passes validation. Only safe on paths that stop before execution
        /// (client routing), since Execute would need a real GameState.
        /// </summary>
        private class PassingCommand : BaseCommand
        {
            public int Payload;

            public override bool Validate(GameState gameState) => true;

            public override void Execute(GameState gameState) => throw new InvalidOperationException(
                "Execute requires a real GameState - this command must only be used on " +
                "paths that route away before executing");

            public override void Undo(GameState gameState) { }

            public override void Serialize(BinaryWriter writer) => writer.Write(Payload);

            public override void Deserialize(BinaryReader reader) => Payload = reader.ReadInt32();
        }

        private class FakeNetworkBridge : INetworkBridge
        {
            public bool IsHost { get; set; }
            public bool IsConnected { get; set; } = true;

            public readonly List<byte[]> Broadcasts = new List<byte[]>();
            public readonly List<byte[]> SentToHost = new List<byte[]>();

            public void BroadcastCommand(byte[] commandData, uint tick) => Broadcasts.Add(commandData);
            public void SendCommandToHost(byte[] commandData, uint tick) => SentToHost.Add(commandData);
            public void BroadcastChecksum(uint tick, uint checksum) { }
            public void SendStateToPeer(int peerId, byte[] stateData, uint tick) { }

            public event Action<int, byte[], uint> OnCommandReceived;

            // Required by the interface but not exercised here; explicit add/remove
            // accessors keep the compiler quiet without a fake raiser method.
            public event Action<byte[], uint> OnStateReceived { add { } remove { } }
            public event Action<int, uint, uint> OnChecksumReceived { add { } remove { } }
            public event Action<int> OnStateSyncRequested { add { } remove { } }

            /// <summary>Simulates a command arriving from the network.</summary>
            public void RaiseCommandReceived(int peerId, byte[] data, uint tick)
                => OnCommandReceived?.Invoke(peerId, data, tick);

            /// <summary>True while the processor is subscribed to this bridge.</summary>
            public bool HasCommandSubscriber => OnCommandReceived != null;
        }

        private CommandProcessor processor;
        private GameObject gameStateObject;

        /// <summary>
        /// The constructor rejects a null GameState, so tests need a real instance. The
        /// GameObject is created INACTIVE so Awake never runs - GameState.Awake would
        /// otherwise call InitializeSystems and allocate native memory for 65536
        /// provinces on every test.
        ///
        /// The reference is only stored, never dereferenced, on the paths tested here.
        /// Any test that reaches command execution will NullReference on
        /// gameState.EventBus - that is the boundary of what EditMode can cover.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            gameStateObject = new GameObject("TestGameState");
            gameStateObject.SetActive(false);

            var gameState = gameStateObject.AddComponent<GameState>();
            processor = new CommandProcessor(gameState);
        }

        [TearDown]
        public void TearDown()
        {
            processor?.Dispose();

            if (gameStateObject != null)
                UnityEngine.Object.DestroyImmediate(gameStateObject);
        }

        // ===== Construction =====

        [Test]
        public void Constructor_WithNullGameState_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CommandProcessor(null),
                "A processor without game state could not validate or execute anything");
        }

        // ===== Single-player defaults =====

        [Test]
        public void WithoutNetworkBridge_IsNotMultiplayer()
        {
            Assert.IsFalse(processor.IsMultiplayer,
                "No bridge means single-player");
        }

        [Test]
        public void WithoutNetworkBridge_IsAuthoritative()
        {
            Assert.IsTrue(processor.IsAuthoritative,
                "Single-player is always authoritative over its own state");
        }

        [Test]
        public void WithDisconnectedBridge_IsNotMultiplayer()
        {
            processor.SetNetworkBridge(new FakeNetworkBridge { IsConnected = false, IsHost = false });

            Assert.IsFalse(processor.IsMultiplayer,
                "A bridge that is not connected must not count as multiplayer");
            Assert.IsTrue(processor.IsAuthoritative,
                "Falling back to single-player means falling back to authoritative");
        }

        [Test]
        public void AsHost_IsMultiplayerAndAuthoritative()
        {
            processor.SetNetworkBridge(new FakeNetworkBridge { IsConnected = true, IsHost = true });

            Assert.IsTrue(processor.IsMultiplayer);
            Assert.IsTrue(processor.IsAuthoritative);
        }

        [Test]
        public void AsClient_IsMultiplayerButNotAuthoritative()
        {
            processor.SetNetworkBridge(new FakeNetworkBridge { IsConnected = true, IsHost = false });

            Assert.IsTrue(processor.IsMultiplayer);
            Assert.IsFalse(processor.IsAuthoritative,
                "A client must never consider itself authoritative");
        }

        // ===== Bridge lifecycle =====

        [Test]
        public void SetNetworkBridge_SubscribesToIncomingCommands()
        {
            var bridge = new FakeNetworkBridge();

            processor.SetNetworkBridge(bridge);

            Assert.IsTrue(bridge.HasCommandSubscriber,
                "The processor must listen for commands arriving from the network");
        }

        [Test]
        public void SetNetworkBridge_UnsubscribesFromThePreviousBridge()
        {
            var first = new FakeNetworkBridge();
            processor.SetNetworkBridge(first);

            processor.SetNetworkBridge(new FakeNetworkBridge());

            Assert.IsFalse(first.HasCommandSubscriber,
                "Leaving a stale subscription on the old bridge would double-execute " +
                "commands after a reconnect");
        }

        [Test]
        public void SetNetworkBridge_ToNull_ReturnsToSinglePlayer()
        {
            processor.SetNetworkBridge(new FakeNetworkBridge { IsConnected = true, IsHost = false });

            processor.SetNetworkBridge(null);

            Assert.IsFalse(processor.IsMultiplayer);
            Assert.IsTrue(processor.IsAuthoritative);
        }

        [Test]
        public void Dispose_DetachesTheBridge()
        {
            var bridge = new FakeNetworkBridge();
            processor.SetNetworkBridge(bridge);

            processor.Dispose();

            Assert.IsFalse(bridge.HasCommandSubscriber,
                "A disposed processor must not keep receiving network commands");
        }

        [Test]
        public void Dispose_IsIdempotent()
        {
            processor.Dispose();

            Assert.DoesNotThrow(() => processor.Dispose());
        }

        [Test]
        public void SubmitCommand_AfterDispose_ReturnsFalse()
        {
            processor.RegisterCommandType<RejectingCommand>();
            processor.Dispose();

            Assert.IsFalse(processor.SubmitCommand(new RejectingCommand()),
                "A disposed processor must reject work rather than acting on it");
        }

        [Test]
        public void SubmitCommandWithMessage_AfterDispose_ExplainsWhy()
        {
            processor.RegisterCommandType<RejectingCommand>();
            processor.Dispose();

            processor.SubmitCommand(new RejectingCommand(), out string message);

            Assert.IsNotNull(message);
            Assert.IsNotEmpty(message, "The caller should be told why the command was dropped");
        }

        // ===== Registration =====

        /// <summary>
        /// Type IDs are assigned sequentially from registration order and travel over the
        /// wire. This is the rule both peers must agree on; a mismatch decodes commands
        /// as the wrong type without any error.
        /// </summary>
        [Test]
        public void RegisterCommandType_AssignsIdsInRegistrationOrder()
        {
            // Register a decoy first so PassingCommand cannot accidentally land on ID 1.
            processor.RegisterCommandType<RejectingCommand>();
            processor.RegisterCommandType<PassingCommand>();

            var bridge = new FakeNetworkBridge { IsConnected = true, IsHost = false };
            processor.SetNetworkBridge(bridge);

            // Client routing serializes without executing, which exposes the assigned ID.
            processor.SubmitCommand(new PassingCommand { Payload = 1 });

            byte[] data = bridge.SentToHost[0];
            ushort typeId = (ushort)(data[0] | (data[1] << 8));

            Assert.AreEqual(2, typeId,
                "The second registered type must be ID 2 - IDs are sequential from 1 in " +
                "registration order, which is what both peers must agree on");
        }

        /// <summary>
        /// Registration order is an unenforced cross-client contract: IDs come from a
        /// counter, so registering the same types in a different order on two peers makes
        /// each decode the other's commands as the wrong type, silently. This pins the
        /// assignment rule that both sides depend on.
        /// </summary>
        [Test]
        public void RegisterCommandType_AssignsSequentialIdsStartingAtOne()
        {
            processor.RegisterCommandType<SecondRejectingCommand>();
            processor.RegisterCommandType<ThirdRejectingCommand>();
            processor.RegisterCommandType<PassingCommand>();

            var bridge = new FakeNetworkBridge { IsConnected = true, IsHost = false };
            processor.SetNetworkBridge(bridge);

            processor.SubmitCommand(new PassingCommand { Payload = 1 });

            byte[] data = bridge.SentToHost[0];
            ushort typeId = (ushort)(data[0] | (data[1] << 8));

            Assert.AreEqual(3, typeId,
                "The third registered type must be ID 3. ID 0 is never assigned, so a " +
                "zero type ID on the wire means malformed data rather than a valid command.");
        }

        [Test]
        public void RegisterCommandType_DuplicateRegistration_IsIgnored()
        {
            processor.RegisterCommandType<RejectingCommand>();

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("already registered"));

            Assert.DoesNotThrow(() => processor.RegisterCommandType<RejectingCommand>(),
                "Re-registering must warn rather than throw or shift every later ID");
        }

        // ===== Registration checksum =====

        [Test]
        public void RegistrationChecksum_MatchesForIdenticalRegistrationOrder()
        {
            var other = new CommandProcessor(gameStateObject.GetComponent<GameState>());
            try
            {
                processor.RegisterCommandType<RejectingCommand>();
                processor.RegisterCommandType<SecondRejectingCommand>();

                other.RegisterCommandType<RejectingCommand>();
                other.RegisterCommandType<SecondRejectingCommand>();

                Assert.AreEqual(processor.GetRegistrationChecksum(), other.GetRegistrationChecksum(),
                    "Peers that registered the same types in the same order must agree");
            }
            finally
            {
                other.Dispose();
            }
        }

        /// <summary>
        /// The case the checksum exists for: same types, different order means different
        /// IDs, which means each peer decodes the other's commands as the wrong type.
        /// </summary>
        [Test]
        public void RegistrationChecksum_DiffersWhenOrderDiffers()
        {
            var other = new CommandProcessor(gameStateObject.GetComponent<GameState>());
            try
            {
                processor.RegisterCommandType<RejectingCommand>();
                processor.RegisterCommandType<SecondRejectingCommand>();

                other.RegisterCommandType<SecondRejectingCommand>();
                other.RegisterCommandType<RejectingCommand>();

                Assert.AreNotEqual(processor.GetRegistrationChecksum(), other.GetRegistrationChecksum(),
                    "Swapped registration order assigns swapped IDs and must be detected");
            }
            finally
            {
                other.Dispose();
            }
        }

        /// <summary>
        /// Catches a peer on an older build that does not know about a newer command.
        /// </summary>
        [Test]
        public void RegistrationChecksum_DiffersWhenATypeIsMissing()
        {
            var other = new CommandProcessor(gameStateObject.GetComponent<GameState>());
            try
            {
                processor.RegisterCommandType<RejectingCommand>();
                processor.RegisterCommandType<SecondRejectingCommand>();

                other.RegisterCommandType<RejectingCommand>();

                Assert.AreNotEqual(processor.GetRegistrationChecksum(), other.GetRegistrationChecksum(),
                    "A peer missing a command type must not be allowed to connect");
            }
            finally
            {
                other.Dispose();
            }
        }

        [Test]
        public void RegistrationChecksum_DiffersWhenAnExtraTypeIsPresent()
        {
            var other = new CommandProcessor(gameStateObject.GetComponent<GameState>());
            try
            {
                processor.RegisterCommandType<RejectingCommand>();

                other.RegisterCommandType<RejectingCommand>();
                other.RegisterCommandType<ThirdRejectingCommand>();

                Assert.AreNotEqual(processor.GetRegistrationChecksum(), other.GetRegistrationChecksum(),
                    "An extra command type (for example from a mod) must be detected");
            }
            finally
            {
                other.Dispose();
            }
        }

        [Test]
        public void RegistrationChecksum_IsStableAcrossCalls()
        {
            processor.RegisterCommandType<RejectingCommand>();
            processor.RegisterCommandType<SecondRejectingCommand>();

            Assert.AreEqual(processor.GetRegistrationChecksum(), processor.GetRegistrationChecksum(),
                "A checksum that varies between calls would reject every connection");
        }

        [Test]
        public void RegistrationChecksum_ChangesAsTypesAreRegistered()
        {
            uint empty = processor.GetRegistrationChecksum();

            processor.RegisterCommandType<RejectingCommand>();
            uint afterFirst = processor.GetRegistrationChecksum();

            processor.RegisterCommandType<SecondRejectingCommand>();
            uint afterSecond = processor.GetRegistrationChecksum();

            Assert.AreNotEqual(empty, afterFirst, "Registering a type must change the checksum");
            Assert.AreNotEqual(afterFirst, afterSecond, "So must registering a second type");
        }

        [Test]
        public void RegisteredCommandCount_TracksRegistrations()
        {
            Assert.AreEqual(0, processor.RegisteredCommandCount);

            processor.RegisterCommandType<RejectingCommand>();
            processor.RegisterCommandType<SecondRejectingCommand>();

            Assert.AreEqual(2, processor.RegisteredCommandCount);
        }

        [Test]
        public void RegisteredCommandCount_IgnoresDuplicateRegistration()
        {
            processor.RegisterCommandType<RejectingCommand>();

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("already registered"));
            processor.RegisterCommandType<RejectingCommand>();

            Assert.AreEqual(1, processor.RegisteredCommandCount,
                "A duplicate must not consume a second ID - that would shift every later ID");
        }

        // ===== Validation gate =====

        [Test]
        public void SubmitCommand_WhenValidationFails_ReturnsFalse()
        {
            processor.RegisterCommandType<RejectingCommand>();

            Assert.IsFalse(processor.SubmitCommand(new RejectingCommand()),
                "A command that fails validation must not report success");
        }

        [Test]
        public void SubmitCommand_WhenValidationFails_DoesNotSerialize()
        {
            processor.RegisterCommandType<RejectingCommand>();
            processor.SetNetworkBridge(new FakeNetworkBridge { IsConnected = true, IsHost = false });

            var command = new RejectingCommand();
            processor.SubmitCommand(command);

            Assert.AreEqual(0, command.SerializeCallCount,
                "An invalid command must be rejected before it reaches the wire");
        }

        [Test]
        public void SubmitCommand_WhenValidationFails_ReportsAnError()
        {
            processor.RegisterCommandType<RejectingCommand>();

            processor.SubmitCommand(new RejectingCommand(), out string message);

            Assert.IsNotEmpty(message, "A rejection should explain itself");
        }

        [Test]
        public void SubmitCommand_UnregisteredType_DoesNotReachTheNetwork()
        {
            var bridge = new FakeNetworkBridge { IsConnected = true, IsHost = false };
            processor.SetNetworkBridge(bridge);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("not registered for networking"));

            // RejectingCommand is never registered here, so it falls back to local-only.
            // Validation then fails, stopping before execution.
            processor.SubmitCommand(new RejectingCommand());

            Assert.AreEqual(0, bridge.SentToHost.Count,
                "An unregistered command cannot be networked - it must stay local");
        }

        // ===== Client routing =====

        /// <summary>
        /// A client must not execute commands itself - it sends them to the host and
        /// waits. This is the one execution-adjacent path reachable without a GameState,
        /// because routing happens before Execute.
        /// </summary>
        [Test]
        public void AsClient_ValidCommand_IsSentToHostAndNotExecuted()
        {
            processor.RegisterCommandType<PassingCommand>();
            var bridge = new FakeNetworkBridge { IsConnected = true, IsHost = false };
            processor.SetNetworkBridge(bridge);

            // PassingCommand.Execute throws; reaching it would fail this test.
            bool result = processor.SubmitCommand(new PassingCommand { Payload = 7 });

            Assert.IsTrue(result, "The client reports success optimistically");
            Assert.AreEqual(1, bridge.SentToHost.Count, "The command must go to the host");
            Assert.AreEqual(0, bridge.Broadcasts.Count, "A client must never broadcast");
        }

        [Test]
        public void AsClient_SentPayload_CarriesTheTypeIdPrefix()
        {
            processor.RegisterCommandType<PassingCommand>();
            var bridge = new FakeNetworkBridge { IsConnected = true, IsHost = false };
            processor.SetNetworkBridge(bridge);

            processor.SubmitCommand(new PassingCommand { Payload = 12345 });

            byte[] data = bridge.SentToHost[0];

            Assert.GreaterOrEqual(data.Length, 2, "Every payload starts with a 2-byte type ID");

            ushort typeId = (ushort)(data[0] | (data[1] << 8));
            Assert.AreEqual(1, typeId,
                "The first registered type must be ID 1 - IDs start at 1, not 0");
        }

        /// <summary>
        /// The wire format is [typeId:2][command payload]. The reader reconstructs the
        /// type ID with a manual little-endian read while the writer uses BinaryWriter -
        /// the two must agree, including above 255 where a byte-order error first shows.
        /// </summary>
        [Test]
        public void SentPayload_RoundTripsThroughTheWireFormat()
        {
            processor.RegisterCommandType<PassingCommand>();
            var bridge = new FakeNetworkBridge { IsConnected = true, IsHost = false };
            processor.SetNetworkBridge(bridge);

            processor.SubmitCommand(new PassingCommand { Payload = 987654 });

            byte[] data = bridge.SentToHost[0];

            var restored = new PassingCommand();
            using (var stream = new MemoryStream(data, 2, data.Length - 2))
            using (var reader = new BinaryReader(stream))
            {
                restored.Deserialize(reader);
            }

            Assert.AreEqual(987654, restored.Payload,
                "The payload must survive the round-trip after skipping the type ID");
        }

        [Test]
        public void AsClient_MultipleCommands_AreAllForwarded()
        {
            processor.RegisterCommandType<PassingCommand>();
            var bridge = new FakeNetworkBridge { IsConnected = true, IsHost = false };
            processor.SetNetworkBridge(bridge);

            processor.SubmitCommand(new PassingCommand { Payload = 1 });
            processor.SubmitCommand(new PassingCommand { Payload = 2 });
            processor.SubmitCommand(new PassingCommand { Payload = 3 });

            Assert.AreEqual(3, bridge.SentToHost.Count, "No command may be dropped silently");
        }

        // ===== Remote command rejection =====

        [Test]
        public void RemoteCommand_WithNullData_IsIgnored()
        {
            var bridge = new FakeNetworkBridge { IsConnected = true, IsHost = false };
            processor.SetNetworkBridge(bridge);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Invalid command data"));

            Assert.DoesNotThrow(() => bridge.RaiseCommandReceived(1, null, 0),
                "Malformed network input must never crash the processor");
        }

        [Test]
        public void RemoteCommand_TooShortForATypeId_IsIgnored()
        {
            var bridge = new FakeNetworkBridge { IsConnected = true, IsHost = false };
            processor.SetNetworkBridge(bridge);

            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Invalid command data"));

            Assert.DoesNotThrow(() => bridge.RaiseCommandReceived(1, new byte[] { 5 }, 0),
                "A single byte cannot contain a type ID");
        }

        [Test]
        public void RemoteCommand_WithUnknownTypeId_IsIgnored()
        {
            var bridge = new FakeNetworkBridge { IsConnected = true, IsHost = false };
            processor.SetNetworkBridge(bridge);

            // Type ID 9999 was never registered.
            var data = new byte[] { 0x0F, 0x27, 0, 0, 0, 0 };

            Assert.DoesNotThrow(() => bridge.RaiseCommandReceived(1, data, 0),
                "An unknown type ID must be skipped, not guessed at");
        }
    }
}
