using NUnit.Framework;
using Unity.Collections;
using Map.Simulation;
using Map.Simulation.Commands;

namespace Map.Tests.Simulation
{
    /// <summary>
    /// Tests for the command system (Task 1.4)
    /// Validates multiplayer foundation components: commands, serialization, deterministic RNG, state validation
    /// </summary>
    [TestFixture]
    public class CommandSystemTests
    {
        private ProvinceSimulation testSimulation;
        private CommandProcessor commandProcessor;

        [SetUp]
        public void Setup()
        {
            testSimulation = new ProvinceSimulation(10);
            testSimulation.AddProvince(1, TerrainType.Grassland);
            testSimulation.AddProvince(2, TerrainType.Hills);
            testSimulation.AddProvince(3, TerrainType.Forest);

            commandProcessor = new CommandProcessor(testSimulation);
        }

        [TearDown]
        public void Teardown()
        {
            commandProcessor?.Dispose();
            testSimulation?.Dispose();
        }

        [Test]
        public void ChangeOwnerCommand_ValidCommand_ShouldExecuteSuccessfully()
        {
            var command = new ChangeOwnerCommand(1, 100, 1, 50, 50);

            var validationResult = command.Validate(testSimulation);
            Assert.IsTrue(validationResult.IsValid, "Command should be valid");

            var executionResult = command.Execute(testSimulation);
            Assert.IsTrue(executionResult.Success, "Command should execute successfully");

            var newState = testSimulation.GetProvinceState(1);
            Assert.AreEqual(50, newState.ownerID, "Owner should be updated");
            Assert.AreEqual(50, newState.controllerID, "Controller should be updated");

            executionResult.Dispose();
        }

        [Test]
        public void ChangeOwnerCommand_InvalidProvince_ShouldFailValidation()
        {
            var command = new ChangeOwnerCommand(1, 100, 999, 50, 50); // Province 999 doesn't exist

            var validationResult = command.Validate(testSimulation);
            Assert.IsFalse(validationResult.IsValid, "Command should fail validation");
            Assert.AreEqual(CommandValidationError.InvalidProvince, validationResult.Error);
        }

        [Test]
        public void ChangeOwnerCommand_InvalidOwnerID_ShouldFailValidation()
        {
            var command = new ChangeOwnerCommand(1, 100, 1, 999, 999); // Owner ID too high

            var validationResult = command.Validate(testSimulation);
            Assert.IsFalse(validationResult.IsValid, "Command should fail validation");
            Assert.AreEqual(CommandValidationError.InvalidParameters, validationResult.Error);
        }

        [Test]
        public void CommandSerialization_RoundTrip_ShouldPreserveData()
        {
            var originalCommand = new ChangeOwnerCommand(123, 456, 1, 50, 60);
            var buffer = new NativeArray<byte>(originalCommand.GetSerializedSize(), Allocator.Temp);

            try
            {
                // Serialize
                int bytesWritten = originalCommand.Serialize(buffer, 0);
                Assert.AreEqual(originalCommand.GetSerializedSize(), bytesWritten, "Should write expected number of bytes");

                // Deserialize
                var deserializedCommand = new ChangeOwnerCommand();
                int bytesRead = deserializedCommand.Deserialize(buffer, 0);
                Assert.AreEqual(bytesWritten, bytesRead, "Should read same number of bytes as written");

                // Compare data
                Assert.AreEqual(originalCommand.ExecutionTick, deserializedCommand.ExecutionTick, "ExecutionTick should match");
                Assert.AreEqual(originalCommand.PlayerID, deserializedCommand.PlayerID, "PlayerID should match");
                Assert.AreEqual(originalCommand.provinceID, deserializedCommand.provinceID, "ProvinceID should match");
                Assert.AreEqual(originalCommand.newOwnerID, deserializedCommand.newOwnerID, "NewOwnerID should match");
                Assert.AreEqual(originalCommand.newControllerID, deserializedCommand.newControllerID, "NewControllerID should match");
            }
            finally
            {
                if (buffer.IsCreated) buffer.Dispose();
            }
        }

        [Test]
        public void CommandProcessor_SubmitAndProcess_ShouldExecuteCommands()
        {
            var command = new ChangeOwnerCommand(1, 100, 1, 75, 75);

            var submissionResult = commandProcessor.SubmitCommand(command);
            Assert.IsTrue(submissionResult.Success, "Command submission should succeed");

            var tickResult = commandProcessor.ProcessTick();
            Assert.AreEqual(1, tickResult.CommandsExecuted, "Should execute one command");
            Assert.AreEqual(0, tickResult.CommandsRejected, "Should reject no commands");

            var newState = testSimulation.GetProvinceState(1);
            Assert.AreEqual(75, newState.ownerID, "Province owner should be updated");
        }

        [Test]
        public void CommandProcessor_InvalidCommand_ShouldReject()
        {
            var command = new ChangeOwnerCommand(1, 100, 999, 75, 75); // Invalid province

            var submissionResult = commandProcessor.SubmitCommand(command);
            Assert.IsFalse(submissionResult.Success, "Invalid command submission should fail");
        }

        [Test]
        public void CommandBatchSerialization_MultipleCommands_ShouldPreserveOrder()
        {
            var commands = new IProvinceCommand[]
            {
                new ChangeOwnerCommand(1, 100, 1, 10, 10),
                new ChangeOwnerCommand(1, 200, 2, 20, 20),
                new ChangeOwnerCommand(1, 300, 3, 30, 30)
            };

            var batchResult = CommandSerializer.SerializeCommandBatch(commands, 1, 100, Allocator.Temp);
            Assert.IsTrue(batchResult.Success, "Batch serialization should succeed");

            try
            {
                var deserializeResult = CommandSerializer.DeserializeCommandBatch(batchResult.Buffer);
                Assert.IsTrue(deserializeResult.Success, "Batch deserialization should succeed");

                Assert.AreEqual(3, deserializeResult.Commands.Count, "Should have 3 commands");
                Assert.AreEqual(1, deserializeResult.Header.Tick, "Header tick should match");
                Assert.AreEqual(100, deserializeResult.Header.PlayerID, "Header player ID should match");

                // Verify command data
                var cmd1 = (ChangeOwnerCommand)deserializeResult.Commands[0];
                var cmd2 = (ChangeOwnerCommand)deserializeResult.Commands[1];
                var cmd3 = (ChangeOwnerCommand)deserializeResult.Commands[2];

                Assert.AreEqual(1, cmd1.provinceID, "First command province should match");
                Assert.AreEqual(2, cmd2.provinceID, "Second command province should match");
                Assert.AreEqual(3, cmd3.provinceID, "Third command province should match");

                // Cleanup
                foreach (var cmd in deserializeResult.Commands)
                    cmd.Dispose();
            }
            finally
            {
                if (batchResult.Buffer.IsCreated) batchResult.Buffer.Dispose();
            }
        }

        [Test]
        public void DeterministicRandom_SameSeed_ShouldProduceSameSequence()
        {
            var rng1 = new DeterministicRandom(12345);
            var rng2 = new DeterministicRandom(12345);

            for (int i = 0; i < 100; i++)
            {
                uint value1 = rng1.NextUInt();
                uint value2 = rng2.NextUInt();
                Assert.AreEqual(value1, value2, $"Random values should match at iteration {i}");
            }
        }

        [Test]
        public void DeterministicRandom_DifferentSeeds_ShouldProduceDifferentSequences()
        {
            var rng1 = new DeterministicRandom(12345);
            var rng2 = new DeterministicRandom(54321);

            bool foundDifference = false;
            for (int i = 0; i < 10; i++)
            {
                uint value1 = rng1.NextUInt();
                uint value2 = rng2.NextUInt();
                if (value1 != value2)
                {
                    foundDifference = true;
                    break;
                }
            }

            Assert.IsTrue(foundDifference, "Different seeds should produce different sequences");
        }

        [Test]
        public void DeterministicRandom_Range_ShouldStayInBounds()
        {
            var rng = new DeterministicRandom(12345);

            for (int i = 0; i < 100; i++)
            {
                uint value = rng.NextUInt(10);
                Assert.Less(value, 10U, "Random value should be less than max");

                int intValue = rng.NextInt(5, 15);
                Assert.GreaterOrEqual(intValue, 5, "Random int should be >= min");
                Assert.Less(intValue, 15, "Random int should be < max");
            }
        }

        [Test]
        public void StateValidator_ValidSimulation_ShouldCalculateChecksum()
        {
            var checksum = StateValidator.CalculateStateChecksum(testSimulation);

            Assert.IsTrue(checksum.IsValid, "Checksum should be valid");
            Assert.AreEqual(3U, checksum.ProvinceCount, "Province count should match");
            Assert.Greater(checksum.MainHash, 0U, "Main hash should be non-zero");
        }

        [Test]
        public void StateValidator_IdenticalStates_ShouldHaveIdenticalChecksums()
        {
            var checksum1 = StateValidator.CalculateStateOnlyChecksum(testSimulation);

            // Make same changes to simulation
            testSimulation.SetProvinceOwner(1, 100);
            var checksum2 = StateValidator.CalculateStateOnlyChecksum(testSimulation);

            // Reset and make same changes again
            testSimulation.SetProvinceOwner(1, 0);
            testSimulation.SetProvinceOwner(1, 100);
            var checksum3 = StateValidator.CalculateStateOnlyChecksum(testSimulation);

            Assert.AreEqual(checksum2, checksum3, "Identical states should have identical checksums");
            Assert.AreNotEqual(checksum1, checksum2, "Different states should have different checksums");
        }

        [Test]
        public void StateValidator_ValidateSimulation_ShouldPassValidation()
        {
            var validation = StateValidator.ValidateSimulationState(testSimulation);

            Assert.IsTrue(validation.IsValid, "Valid simulation should pass validation");
            Assert.AreEqual(0, validation.Issues.Count, "Should have no validation issues");
            Assert.AreEqual(3, validation.ProvinceCount, "Should report correct province count");
        }

        [Test]
        public void FixedPointArithmetic_ShouldBeDeterministic()
        {
            var fp1 = FixedPoint32.FromInt(10);
            var fp2 = FixedPoint32.FromInt(3);

            var result1 = fp1 * fp2;
            var result2 = fp1 * fp2;

            Assert.AreEqual(result1.RawValue, result2.RawValue, "Fixed-point multiplication should be deterministic");

            var fpSum1 = fp1 + fp2;
            var fpSum2 = fp1 + fp2;

            Assert.AreEqual(fpSum1.RawValue, fpSum2.RawValue, "Fixed-point addition should be deterministic");
        }

        [Test]
        public void CommandChecksum_SameCommand_ShouldHaveSameChecksum()
        {
            var command1 = new ChangeOwnerCommand(123, 456, 1, 50, 60);
            var command2 = new ChangeOwnerCommand(123, 456, 1, 50, 60);

            uint checksum1 = command1.GetChecksum();
            uint checksum2 = command2.GetChecksum();

            Assert.AreEqual(checksum1, checksum2, "Identical commands should have identical checksums");
        }

        [Test]
        public void CommandChecksum_DifferentCommand_ShouldHaveDifferentChecksum()
        {
            var command1 = new ChangeOwnerCommand(123, 456, 1, 50, 60);
            var command2 = new ChangeOwnerCommand(123, 456, 1, 51, 60); // Different owner

            uint checksum1 = command1.GetChecksum();
            uint checksum2 = command2.GetChecksum();

            Assert.AreNotEqual(checksum1, checksum2, "Different commands should have different checksums");
        }

        [Test]
        public void DeterministicRandom_Branching_ShouldCreateIndependentStreams()
        {
            var mainRng = new DeterministicRandom(12345);
            var branchedRng = mainRng.Branch(1);

            // Generate values from both
            var mainValues = new uint[10];
            var branchedValues = new uint[10];

            for (int i = 0; i < 10; i++)
            {
                mainValues[i] = mainRng.NextUInt();
                branchedValues[i] = branchedRng.NextUInt();
            }

            // Should be different sequences
            bool hasDifferences = false;
            for (int i = 0; i < 10; i++)
            {
                if (mainValues[i] != branchedValues[i])
                {
                    hasDifferences = true;
                    break;
                }
            }

            Assert.IsTrue(hasDifferences, "Branched RNG should produce different sequence");

            // But same seed should reproduce same branch
            var testRng = new DeterministicRandom(12345);
            var testBranch = testRng.Branch(1);

            for (int i = 0; i < 10; i++)
            {
                uint branchedValue = testBranch.NextUInt();
                Assert.AreEqual(branchedValues[i], branchedValue, $"Branched RNG should be reproducible at iteration {i}");
            }
        }

        [Test]
        public void CommandSerializer_UnknownCommandType_ShouldFailDeserialization()
        {
            var buffer = new NativeArray<byte>(10, Allocator.Temp);
            buffer[0] = 255; // Unknown command type

            try
            {
                var result = CommandSerializer.DeserializeCommand(buffer, 0);
                Assert.IsFalse(result.Success, "Unknown command type should fail deserialization");
            }
            finally
            {
                if (buffer.IsCreated) buffer.Dispose();
            }
        }
    }
}