// Result.cs - Unified result types for success/failure patterns
//
// Usage:
//   Simple:     Result.Success() or Result.Failure("error")
//   With value: Result<T>.Success(value) or Result<T>.Failure("error")
//
// Implicit bool conversion: if (result) { ... }

using System;
using System.Runtime.CompilerServices;

namespace Core.Common
{
    /// <summary>
    /// Lightweight result type for operations that can succeed or fail.
    /// Use when no return value is needed beyond success/failure status.
    /// </summary>
    /// <remarks>
    /// Prefer this over throwing exceptions for expected failure cases.
    /// For operations returning a value, use Result&lt;T&gt; instead.
    /// </remarks>
    public readonly struct Result
    {
        /// <summary>True if the operation succeeded.</summary>
        public bool IsSuccess { get; }

        /// <summary>Error message if failed, null if successful.</summary>
        public string Error { get; }

        /// <summary>True if the operation failed.</summary>
        public bool IsFailure
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !IsSuccess;
        }

        private Result(bool success, string error)
        {
            IsSuccess = success;
            Error = error;
        }

        /// <summary>Creates a successful result.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result Success() => new Result(true, null);

        /// <summary>Creates a failed result with an error message.</summary>
        /// <param name="error">Description of what went wrong.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result Failure(string error) => new Result(false, error ?? "Unknown error");

        /// <summary>Implicit conversion to bool for convenient if-checks.</summary>
        /// <example>
        /// var result = DoSomething();
        /// if (result) { /* success */ }
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator bool(Result result) => result.IsSuccess;

        /// <summary>
        /// Executes an action if the result is successful.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result OnSuccess(Action action)
        {
            if (IsSuccess) action?.Invoke();
            return this;
        }

        /// <summary>
        /// Executes an action if the result is a failure.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result OnFailure(Action<string> action)
        {
            if (IsFailure) action?.Invoke(Error);
            return this;
        }

        public override string ToString() => IsSuccess ? "Success" : $"Failure: {Error}";
    }

    /// <summary>
    /// Result type that carries a value on success.
    /// Use for operations that return a value or fail.
    /// </summary>
    /// <typeparam name="T">Type of the success value.</typeparam>
    /// <remarks>
    /// Access Value only after checking IsSuccess.
    /// For reference types, Value is default(T) on failure.
    /// </remarks>
    public readonly struct Result<T>
    {
        /// <summary>True if the operation succeeded.</summary>
        public bool IsSuccess { get; }

        /// <summary>The result value. Only valid when IsSuccess is true.</summary>
        public T Value { get; }

        /// <summary>Error message if failed, null if successful.</summary>
        public string Error { get; }

        /// <summary>True if the operation failed.</summary>
        public bool IsFailure
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => !IsSuccess;
        }

        private Result(bool success, T value, string error)
        {
            IsSuccess = success;
            Value = value;
            Error = error;
        }

        /// <summary>Creates a successful result with a value.</summary>
        /// <param name="value">The success value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> Success(T value) => new Result<T>(true, value, null);

        /// <summary>Creates a failed result with an error message.</summary>
        /// <param name="error">Description of what went wrong.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Result<T> Failure(string error) => new Result<T>(false, default, error ?? "Unknown error");

        /// <summary>Implicit conversion to bool for convenient if-checks.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator bool(Result<T> result) => result.IsSuccess;

        /// <summary>
        /// Gets the value or returns a default if failed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetValueOrDefault(T defaultValue = default) => IsSuccess ? Value : defaultValue;

        /// <summary>
        /// Tries to get the value, returning false if failed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetValue(out T value)
        {
            value = Value;
            return IsSuccess;
        }

        /// <summary>
        /// Transforms the success value using the provided function.
        /// Returns failure unchanged.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TNew> Map<TNew>(Func<T, TNew> mapper)
        {
            return IsSuccess
                ? Result<TNew>.Success(mapper(Value))
                : Result<TNew>.Failure(Error);
        }

        /// <summary>
        /// Chains another operation that returns a Result.
        /// Only executes if this result is successful.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<TNew> Then<TNew>(Func<T, Result<TNew>> next)
        {
            return IsSuccess ? next(Value) : Result<TNew>.Failure(Error);
        }

        /// <summary>
        /// Executes an action with the value if successful.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<T> OnSuccess(Action<T> action)
        {
            if (IsSuccess) action?.Invoke(Value);
            return this;
        }

        /// <summary>
        /// Executes an action if the result is a failure.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result<T> OnFailure(Action<string> action)
        {
            if (IsFailure) action?.Invoke(Error);
            return this;
        }

        /// <summary>
        /// Converts to a non-generic Result, discarding the value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Result ToResult() => IsSuccess ? Result.Success() : Result.Failure(Error);

        public override string ToString() => IsSuccess ? $"Success: {Value}" : $"Failure: {Error}";
    }

    /// <summary>
    /// Extension methods for Result types.
    /// </summary>
    public static class ResultExtensions
    {
        /// <summary>
        /// Combines multiple results, returning failure if any failed.
        /// </summary>
        public static Result Combine(params Result[] results)
        {
            foreach (var result in results)
            {
                if (result.IsFailure)
                    return result;
            }
            return Result.Success();
        }

        /// <summary>
        /// Combines multiple results, collecting all errors.
        /// </summary>
        public static Result CombineAll(params Result[] results)
        {
            var errors = new System.Text.StringBuilder();
            bool hasFailure = false;

            foreach (var result in results)
            {
                if (result.IsFailure)
                {
                    if (hasFailure) errors.Append("; ");
                    errors.Append(result.Error);
                    hasFailure = true;
                }
            }

            return hasFailure ? Result.Failure(errors.ToString()) : Result.Success();
        }

        /// <summary>
        /// Wraps an action that might throw in a Result.
        /// </summary>
        public static Result Try(Action action)
        {
            try
            {
                action();
                return Result.Success();
            }
            catch (Exception ex)
            {
                return Result.Failure(ex.Message);
            }
        }

        /// <summary>
        /// Wraps a function that might throw in a Result.
        /// </summary>
        public static Result<T> Try<T>(Func<T> func)
        {
            try
            {
                return Result<T>.Success(func());
            }
            catch (Exception ex)
            {
                return Result<T>.Failure(ex.Message);
            }
        }
    }
}
