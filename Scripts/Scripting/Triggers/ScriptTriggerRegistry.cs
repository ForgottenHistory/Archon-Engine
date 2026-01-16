using System;
using System.Collections.Generic;
using Core;

namespace Scripting.Triggers
{
    /// <summary>
    /// Central registry for script triggers and handlers.
    /// Manages registration of trigger types and script handlers.
    ///
    /// ARCHITECTURE: ENGINE owns registry, GAME registers triggers and handlers.
    /// </summary>
    public class ScriptTriggerRegistry : IDisposable
    {
        private readonly Dictionary<string, IScriptTrigger> triggers;
        private readonly Dictionary<string, List<ScriptHandler>> handlers;
        private readonly ScriptEngine scriptEngine;
        private bool isDisposed;

        public ScriptTriggerRegistry(ScriptEngine engine)
        {
            scriptEngine = engine ?? throw new ArgumentNullException(nameof(engine));
            triggers = new Dictionary<string, IScriptTrigger>(StringComparer.OrdinalIgnoreCase);
            handlers = new Dictionary<string, List<ScriptHandler>>(StringComparer.OrdinalIgnoreCase);

            ArchonLogger.Log("ScriptTriggerRegistry initialized", "core_scripting");
        }

        /// <summary>
        /// Register a trigger type
        /// </summary>
        public void RegisterTrigger(IScriptTrigger trigger)
        {
            if (trigger == null)
                throw new ArgumentNullException(nameof(trigger));

            if (triggers.ContainsKey(trigger.TriggerId))
            {
                ArchonLogger.LogWarning($"Trigger '{trigger.TriggerId}' already registered, replacing", "core_scripting");
            }

            triggers[trigger.TriggerId] = trigger;

            // Ensure handler list exists
            if (!handlers.ContainsKey(trigger.TriggerId))
            {
                handlers[trigger.TriggerId] = new List<ScriptHandler>();
            }

            ArchonLogger.Log($"Registered trigger: {trigger.TriggerId}", "core_scripting");
        }

        /// <summary>
        /// Register a script handler for a trigger
        /// </summary>
        public void RegisterHandler(string triggerId, ScriptHandler handler)
        {
            if (string.IsNullOrEmpty(triggerId))
                throw new ArgumentException("TriggerId cannot be null or empty", nameof(triggerId));

            if (!handlers.TryGetValue(triggerId, out var handlerList))
            {
                handlerList = new List<ScriptHandler>();
                handlers[triggerId] = handlerList;
            }

            handlerList.Add(handler);

            // Sort by priority (descending - higher priority first)
            handlerList.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            ArchonLogger.Log($"Registered handler for trigger '{triggerId}' from {handler.Source ?? "unknown"}", "core_scripting");
        }

        /// <summary>
        /// Remove all handlers from a specific source
        /// </summary>
        public void UnregisterHandlersFromSource(string source)
        {
            foreach (var handlerList in handlers.Values)
            {
                handlerList.RemoveAll(h => h.Source == source);
            }
            ArchonLogger.Log($"Unregistered all handlers from source: {source}", "core_scripting");
        }

        /// <summary>
        /// Fire a trigger, executing all registered handlers
        /// </summary>
        /// <param name="triggerId">The trigger to fire</param>
        /// <param name="context">Base context for execution</param>
        /// <returns>Number of handlers executed</returns>
        public int FireTrigger(string triggerId, ScriptContext context)
        {
            if (!triggers.TryGetValue(triggerId, out var trigger))
            {
                ArchonLogger.LogWarning($"Unknown trigger: {triggerId}", "core_scripting");
                return 0;
            }

            if (!handlers.TryGetValue(triggerId, out var handlerList) || handlerList.Count == 0)
            {
                return 0;
            }

            // Check if trigger should fire
            if (!trigger.ShouldFire(context))
            {
                return 0;
            }

            // Get execution context from trigger
            var execContext = trigger.GetExecutionContext(context);

            int executedCount = 0;
            foreach (var handler in handlerList)
            {
                // Check condition if present
                if (!string.IsNullOrEmpty(handler.Condition))
                {
                    if (!scriptEngine.EvaluateBoolCondition(handler.Condition, execContext))
                    {
                        continue; // Skip this handler
                    }
                }

                // Execute the script
                var result = scriptEngine.Execute(handler.Script, execContext);
                if (result.IsSuccess)
                {
                    executedCount++;
                }
                else
                {
                    ArchonLogger.LogWarning($"Handler from {handler.Source} failed: {result.ErrorMessage}", "core_scripting");
                }
            }

            return executedCount;
        }

        /// <summary>
        /// Fire a trigger for each item in a collection (e.g., each province, each country)
        /// </summary>
        public int FireTriggerForEach<T>(string triggerId, ScriptContext baseContext, IEnumerable<T> items, Func<ScriptContext, T, ScriptContext> scopeItem)
        {
            if (!triggers.TryGetValue(triggerId, out var trigger))
            {
                ArchonLogger.LogWarning($"Unknown trigger: {triggerId}", "core_scripting");
                return 0;
            }

            if (!handlers.TryGetValue(triggerId, out var handlerList) || handlerList.Count == 0)
            {
                return 0;
            }

            int totalExecuted = 0;
            foreach (var item in items)
            {
                var itemContext = scopeItem(baseContext, item);

                if (!trigger.ShouldFire(itemContext))
                {
                    continue;
                }

                var execContext = trigger.GetExecutionContext(itemContext);

                foreach (var handler in handlerList)
                {
                    if (!string.IsNullOrEmpty(handler.Condition))
                    {
                        if (!scriptEngine.EvaluateBoolCondition(handler.Condition, execContext))
                        {
                            continue;
                        }
                    }

                    var result = scriptEngine.Execute(handler.Script, execContext);
                    if (result.IsSuccess)
                    {
                        totalExecuted++;
                    }
                }
            }

            return totalExecuted;
        }

        /// <summary>
        /// Get all registered trigger IDs
        /// </summary>
        public IEnumerable<string> GetTriggerIds()
        {
            return triggers.Keys;
        }

        /// <summary>
        /// Get a trigger by ID
        /// </summary>
        public IScriptTrigger GetTrigger(string triggerId)
        {
            return triggers.TryGetValue(triggerId, out var trigger) ? trigger : null;
        }

        /// <summary>
        /// Get handler count for a trigger
        /// </summary>
        public int GetHandlerCount(string triggerId)
        {
            return handlers.TryGetValue(triggerId, out var list) ? list.Count : 0;
        }

        public void Dispose()
        {
            if (isDisposed)
                return;

            triggers.Clear();
            handlers.Clear();
            isDisposed = true;

            ArchonLogger.Log("ScriptTriggerRegistry disposed", "core_scripting");
        }
    }
}
