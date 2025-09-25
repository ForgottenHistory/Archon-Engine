using NUnit.Framework;
using Map.Simulation;

namespace Map.Tests.Simulation
{
    /// <summary>
    /// Tests for province command pattern and serialization system
    /// Validates deterministic multiplayer-ready command execution
    /// </summary>
    [TestFixture]
    public class ProvinceCommandTests
    {
        private ProvinceSimulation simulation;

        [SetUp]
        public void Setup()
        {
            simulation = new ProvinceSimulation(100);
            // Add test provinces
            simulation.AddProvince(1, TerrainType.Grassland);
            simulation.AddProvince(2, TerrainType.Hills);
            simulation.AddProvince(3, TerrainType.Forest);
        }

        [TearDown]
        public void Teardown()
        {
            simulation?.Dispose();
        }

        #region ChangeOwnerCommand Tests

        [Test]
        public void ChangeOwnerCommand_Execution_ShouldUpdateOwnership()
        {
            var command = new ChangeOwnerCommand(100, 1, 200);

            bool result = command.Execute(simulation);

            Assert.IsTrue(result, "Command execution should succeed");

            var state = simulation.GetProvinceState(1);
            Assert.AreEqual(200, state.ownerID, "Owner should be updated");
            Assert.AreEqual(200, state.controllerID, "Controller should match owner by default");
        }

        [Test]
        public void ChangeOwnerCommand_Validation_ShouldRejectOceanProvince()
        {
            var command = new ChangeOwnerCommand(100, 0, 200); // Ocean province

            bool isValid = command.Validate(simulation, out string errorMessage);

            Assert.IsFalse(isValid, "Should reject ocean province");
            Assert.IsNotNull(errorMessage);
            Assert.That(errorMessage.ToLower().Contains("ocean"));
        }

        [Test]
        public void ChangeOwnerCommand_Serialization_ShouldBe9Bytes()
        {
            var command = new ChangeOwnerCommand(12345, 999, 777);

            byte[] serialized = command.Serialize();

            Assert.AreEqual(9, serialized.Length, "Serialized command should be 9 bytes");
            Assert.AreEqual((byte)ProvinceCommandType.ChangeOwner, serialized[0], "First byte should be command type");
        }

        [Test]
        public void ChangeOwnerCommand_SerializationRoundTrip_ShouldPreserveData()
        {
            var originalCommand = new ChangeOwnerCommand(54321, 123, 456);

            byte[] serialized = originalCommand.Serialize();
            var deserializedCommand = (ChangeOwnerCommand)ProvinceCommandSerializer.Deserialize(serialized);

            Assert.AreEqual(originalCommand.executionTick, deserializedCommand.executionTick);
            Assert.AreEqual(originalCommand.provinceID, deserializedCommand.provinceID);
            Assert.AreEqual(originalCommand.newOwnerID, deserializedCommand.newOwnerID);
        }

        #endregion

        #region ChangeControllerCommand Tests

        [Test]
        public void ChangeControllerCommand_Execution_ShouldCreateOccupation()
        {
            // First set owner
            simulation.SetProvinceOwner(1, 100);

            var command = new ChangeControllerCommand(200, 1, 300);

            bool result = command.Execute(simulation);

            Assert.IsTrue(result, "Command execution should succeed");

            var state = simulation.GetProvinceState(1);
            Assert.AreEqual(100, state.ownerID, "Owner should remain unchanged");
            Assert.AreEqual(300, state.controllerID, "Controller should be updated");
            Assert.IsTrue(state.IsOccupied, "Province should be occupied");
        }

        [Test]
        public void ChangeControllerCommand_Validation_ShouldRequireOwnedProvince()
        {
            var command = new ChangeControllerCommand(100, 1, 200); // Province 1 is unowned

            bool isValid = command.Validate(simulation, out string errorMessage);

            Assert.IsFalse(isValid, "Should reject unowned province");
            Assert.IsNotNull(errorMessage);
        }

        [Test]
        public void ChangeControllerCommand_Serialization_ShouldBe9Bytes()
        {
            var command = new ChangeControllerCommand(11111, 888, 999);

            byte[] serialized = command.Serialize();

            Assert.AreEqual(9, serialized.Length, "Serialized command should be 9 bytes");
            Assert.AreEqual((byte)ProvinceCommandType.ChangeController, serialized[0]);
        }

        #endregion

        #region ChangeDevelopmentCommand Tests

        [Test]
        public void ChangeDevelopmentCommand_Execution_ShouldUpdateDevelopment()
        {
            var command = new ChangeDevelopmentCommand(300, 2, 150);

            bool result = command.Execute(simulation);

            Assert.IsTrue(result, "Command execution should succeed");

            var state = simulation.GetProvinceState(2);
            Assert.AreEqual(150, state.development, "Development should be updated");
        }

        [Test]
        public void ChangeDevelopmentCommand_Validation_ShouldRejectOceanTerrain()
        {
            // Add ocean province
            simulation.AddProvince(10, TerrainType.Ocean);

            var command = new ChangeDevelopmentCommand(100, 10, 50);

            bool isValid = command.Validate(simulation, out string errorMessage);

            Assert.IsFalse(isValid, "Should reject ocean terrain development");
            Assert.IsNotNull(errorMessage);
        }

        [Test]
        public void ChangeDevelopmentCommand_Validation_ShouldLimitMountainDevelopment()
        {
            // Add mountain province
            simulation.AddProvince(11, TerrainType.Mountain);

            var command = new ChangeDevelopmentCommand(100, 11, 200); // High development

            bool isValid = command.Validate(simulation, out string errorMessage);

            Assert.IsFalse(isValid, "Should limit mountain development");
            Assert.IsNotNull(errorMessage);
        }

        [Test]
        public void ChangeDevelopmentCommand_Serialization_ShouldBe8Bytes()
        {
            var command = new ChangeDevelopmentCommand(22222, 555, 199);

            byte[] serialized = command.Serialize();

            Assert.AreEqual(8, serialized.Length, "Serialized command should be 8 bytes");
            Assert.AreEqual((byte)ProvinceCommandType.ChangeDevelopment, serialized[0]);
        }

        #endregion

        #region SetFlagCommand Tests

        [Test]
        public void SetFlagCommand_Execution_ShouldSetFlag()
        {
            var command = new SetFlagCommand(400, 3, ProvinceFlags.IsCoastal, true);

            bool result = command.Execute(simulation);

            Assert.IsTrue(result, "Command execution should succeed");

            var state = simulation.GetProvinceState(3);
            Assert.IsTrue(state.HasFlag(ProvinceFlags.IsCoastal), "Flag should be set");
        }

        [Test]
        public void SetFlagCommand_Execution_ShouldClearFlag()
        {
            // First set the flag
            simulation.SetProvinceFlag(3, ProvinceFlags.IsTradeCenter, true);

            var command = new SetFlagCommand(500, 3, ProvinceFlags.IsTradeCenter, false);

            bool result = command.Execute(simulation);

            Assert.IsTrue(result, "Command execution should succeed");

            var state = simulation.GetProvinceState(3);
            Assert.IsFalse(state.HasFlag(ProvinceFlags.IsTradeCenter), "Flag should be cleared");
        }

        [Test]
        public void SetFlagCommand_Validation_ShouldRequireOwnershipForCapital()
        {
            var command = new SetFlagCommand(100, 1, ProvinceFlags.IsCapital, true); // Province 1 is unowned

            bool isValid = command.Validate(simulation, out string errorMessage);

            Assert.IsFalse(isValid, "Should require ownership for capital flag");
            Assert.IsNotNull(errorMessage);
        }

        [Test]
        public void SetFlagCommand_Serialization_ShouldBe9Bytes()
        {
            var command = new SetFlagCommand(33333, 777, ProvinceFlags.HasSpecialBuilding, true);

            byte[] serialized = command.Serialize();

            Assert.AreEqual(9, serialized.Length, "Serialized command should be 9 bytes");
            Assert.AreEqual((byte)ProvinceCommandType.SetFlag, serialized[0]);
        }

        [Test]
        public void SetFlagCommand_SerializationRoundTrip_ShouldPreserveData()
        {
            var originalCommand = new SetFlagCommand(98765, 321, ProvinceFlags.UnderSiege, false);

            byte[] serialized = originalCommand.Serialize();
            var deserializedCommand = (SetFlagCommand)ProvinceCommandSerializer.Deserialize(serialized);

            Assert.AreEqual(originalCommand.executionTick, deserializedCommand.executionTick);
            Assert.AreEqual(originalCommand.provinceID, deserializedCommand.provinceID);
            Assert.AreEqual(originalCommand.flag, deserializedCommand.flag);
            Assert.AreEqual(originalCommand.value, deserializedCommand.value);
        }

        #endregion

        #region Command Interface Tests

        [Test]
        public void Commands_GetCommandType_ShouldReturnCorrectTypes()
        {
            var changeOwner = new ChangeOwnerCommand(1, 1, 1);
            var changeController = new ChangeControllerCommand(1, 1, 1);
            var changeDevelopment = new ChangeDevelopmentCommand(1, 1, 1);
            var setFlag = new SetFlagCommand(1, 1, ProvinceFlags.IsCoastal, true);

            Assert.AreEqual(ProvinceCommandType.ChangeOwner, changeOwner.GetCommandType());
            Assert.AreEqual(ProvinceCommandType.ChangeController, changeController.GetCommandType());
            Assert.AreEqual(ProvinceCommandType.ChangeDevelopment, changeDevelopment.GetCommandType());
            Assert.AreEqual(ProvinceCommandType.SetFlag, setFlag.GetCommandType());
        }

        [Test]
        public void Commands_GetExecutionTick_ShouldReturnCorrectValues()
        {
            uint testTick = 12345;

            var changeOwner = new ChangeOwnerCommand(testTick, 1, 1);
            var changeController = new ChangeControllerCommand(testTick, 1, 1);
            var changeDevelopment = new ChangeDevelopmentCommand(testTick, 1, 1);
            var setFlag = new SetFlagCommand(testTick, 1, ProvinceFlags.IsCoastal, true);

            Assert.AreEqual(testTick, changeOwner.GetExecutionTick());
            Assert.AreEqual(testTick, changeController.GetExecutionTick());
            Assert.AreEqual(testTick, changeDevelopment.GetExecutionTick());
            Assert.AreEqual(testTick, setFlag.GetExecutionTick());
        }

        [Test]
        public void Commands_GetAffectedProvinces_ShouldReturnSingleProvince()
        {
            ushort testProvinceID = 999;

            var changeOwner = new ChangeOwnerCommand(1, testProvinceID, 1);
            var changeController = new ChangeControllerCommand(1, testProvinceID, 1);
            var changeDevelopment = new ChangeDevelopmentCommand(1, testProvinceID, 1);
            var setFlag = new SetFlagCommand(1, testProvinceID, ProvinceFlags.IsCoastal, true);

            Assert.AreEqual(1, changeOwner.GetAffectedProvinces().Length);
            Assert.AreEqual(testProvinceID, changeOwner.GetAffectedProvinces()[0]);

            Assert.AreEqual(1, changeController.GetAffectedProvinces().Length);
            Assert.AreEqual(testProvinceID, changeController.GetAffectedProvinces()[0]);

            Assert.AreEqual(1, changeDevelopment.GetAffectedProvinces().Length);
            Assert.AreEqual(testProvinceID, changeDevelopment.GetAffectedProvinces()[0]);

            Assert.AreEqual(1, setFlag.GetAffectedProvinces().Length);
            Assert.AreEqual(testProvinceID, setFlag.GetAffectedProvinces()[0]);
        }

        #endregion

        #region Serializer Tests

        [Test]
        public void ProvinceCommandSerializer_InvalidCommandType_ShouldThrow()
        {
            byte[] invalidData = { 99, 0, 0, 0, 0, 0, 0, 0, 0 }; // Invalid command type

            Assert.Throws<System.ArgumentException>(() =>
            {
                ProvinceCommandSerializer.Deserialize(invalidData);
            });
        }

        [Test]
        public void ProvinceCommandSerializer_InvalidDataLength_ShouldThrow()
        {
            byte[] tooShort = { (byte)ProvinceCommandType.ChangeOwner }; // Missing data

            Assert.Throws<System.ArgumentException>(() =>
            {
                ProvinceCommandSerializer.Deserialize(tooShort);
            });
        }

        [Test]
        public void ProvinceCommandSerializer_GetCommandSize_ShouldReturnCorrectSizes()
        {
            Assert.AreEqual(9, ProvinceCommandSerializer.GetCommandSize(ProvinceCommandType.ChangeOwner));
            Assert.AreEqual(9, ProvinceCommandSerializer.GetCommandSize(ProvinceCommandType.ChangeController));
            Assert.AreEqual(8, ProvinceCommandSerializer.GetCommandSize(ProvinceCommandType.ChangeDevelopment));
            Assert.AreEqual(9, ProvinceCommandSerializer.GetCommandSize(ProvinceCommandType.SetFlag));
        }

        [Test]
        public void ProvinceCommandSerializer_AllCommandTypes_ShouldDeserializeCorrectly()
        {
            // Test all command types can be serialized and deserialized
            var commands = new IProvinceCommand[]
            {
                new ChangeOwnerCommand(1000, 1, 100),
                new ChangeControllerCommand(2000, 2, 200),
                new ChangeDevelopmentCommand(3000, 3, 150),
                new SetFlagCommand(4000, 1, ProvinceFlags.IsCapital, true)
            };

            foreach (var originalCommand in commands)
            {
                byte[] serialized = originalCommand.Serialize();
                var deserializedCommand = ProvinceCommandSerializer.Deserialize(serialized);

                Assert.AreEqual(originalCommand.GetCommandType(), deserializedCommand.GetCommandType(),
                    $"Command type mismatch for {originalCommand.GetType().Name}");

                Assert.AreEqual(originalCommand.GetExecutionTick(), deserializedCommand.GetExecutionTick(),
                    $"Execution tick mismatch for {originalCommand.GetType().Name}");
            }
        }

        #endregion

        #region Determinism Tests

        [Test]
        public void Commands_RepeatedExecution_ShouldBeDeterministic()
        {
            // Execute same command sequence on two identical simulations
            var simulation2 = new ProvinceSimulation(100);
            simulation2.AddProvince(1, TerrainType.Grassland);
            simulation2.AddProvince(2, TerrainType.Hills);

            var commands = new IProvinceCommand[]
            {
                new ChangeOwnerCommand(100, 1, 200),
                new ChangeDevelopmentCommand(200, 1, 50),
                new SetFlagCommand(300, 1, ProvinceFlags.IsCoastal, true)
            };

            // Execute on both simulations
            foreach (var command in commands)
            {
                command.Execute(simulation);
                command.Execute(simulation2);
            }

            // States should be identical
            var state1 = simulation.GetProvinceState(1);
            var state2 = simulation2.GetProvinceState(1);

            Assert.AreEqual(state1.ownerID, state2.ownerID);
            Assert.AreEqual(state1.development, state2.development);
            Assert.AreEqual(state1.flags, state2.flags);

            // Checksums should match
            Assert.AreEqual(simulation.CalculateStateChecksum(), simulation2.CalculateStateChecksum());

            simulation2.Dispose();
        }

        [Test]
        public void Commands_NetworkBandwidthTarget_ShouldBeEfficient()
        {
            // Validate network efficiency targets
            var commands = new IProvinceCommand[]
            {
                new ChangeOwnerCommand(1, 1, 1),
                new ChangeControllerCommand(1, 1, 1),
                new ChangeDevelopmentCommand(1, 1, 1),
                new SetFlagCommand(1, 1, ProvinceFlags.IsCoastal, true)
            };

            int totalBytes = 0;
            foreach (var command in commands)
            {
                totalBytes += command.Serialize().Length;
            }

            // Total should be under 50 bytes for 4 commands
            Assert.LessOrEqual(totalBytes, 50, "Commands should be network efficient");

            // Verify specific targets from architecture
            Assert.LessOrEqual(totalBytes, 35, "Should meet bandwidth targets (8-9 bytes per command)");
        }

        #endregion
    }
}