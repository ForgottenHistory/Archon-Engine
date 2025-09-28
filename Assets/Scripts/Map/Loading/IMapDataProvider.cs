using System.Threading.Tasks;
using ParadoxParser.Jobs;
using Core;

namespace Map.Loading
{
    /// <summary>
    /// Interface for providing map data to the presentation layer
    /// Abstracts data source (files vs simulation) for clean separation of concerns
    /// </summary>
    public interface IMapDataProvider
    {
        /// <summary>
        /// Load province map data from simulation systems (preferred method)
        /// Follows dual-layer architecture by getting data from Core layer
        /// </summary>
        /// <param name="simulationData">Event data containing simulation state</param>
        /// <param name="bitmapPath">Path to province bitmap file</param>
        /// <param name="csvPath">Path to definition CSV file</param>
        /// <param name="useDefinition">Whether to use CSV definition file</param>
        /// <returns>Province map result for presentation layer</returns>
        Task<ProvinceMapResult?> LoadFromSimulationAsync(SimulationDataReadyEvent simulationData, string bitmapPath, string csvPath, bool useDefinition);

        /// <summary>
        /// Load province map data directly from files (legacy/standalone method)
        /// Used when simulation layer is not available
        /// </summary>
        /// <param name="bitmapPath">Path to province bitmap file</param>
        /// <param name="csvPath">Path to definition CSV file</param>
        /// <param name="useDefinition">Whether to use CSV definition file</param>
        /// <returns>Province map result for presentation layer</returns>
        Task<ProvinceMapResult?> LoadFromFilesAsync(string bitmapPath, string csvPath, bool useDefinition);
    }
}