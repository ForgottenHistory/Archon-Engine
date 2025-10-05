using System.Collections;

namespace Core.Initialization
{
    /// <summary>
    /// Interface for initialization phases in the engine startup sequence
    /// Each phase is responsible for a specific aspect of initialization
    /// </summary>
    public interface IInitializationPhase
    {
        /// <summary>
        /// Human-readable name of this phase (for logging and progress tracking)
        /// </summary>
        string PhaseName { get; }

        /// <summary>
        /// Progress percentage range this phase covers (0-100)
        /// </summary>
        float ProgressStart { get; }
        float ProgressEnd { get; }

        /// <summary>
        /// Execute this initialization phase
        /// Returns IEnumerator for coroutine-based async execution
        /// </summary>
        /// <param name="context">Shared initialization context</param>
        /// <returns>Coroutine enumerator</returns>
        IEnumerator ExecuteAsync(InitializationContext context);

        /// <summary>
        /// Rollback this phase if initialization fails
        /// Used for cleanup and error recovery
        /// </summary>
        /// <param name="context">Shared initialization context</param>
        void Rollback(InitializationContext context);
    }
}
