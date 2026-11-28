namespace AslHelp.Engines;

/// <summary>
///     Provides the steps of <see cref="IMemberPath"/> on the result of a step, so a caller writes 1 step after the other,
///     like <c>root.Path.Member("Player").Member("Health").Value&lt;int&gt;()</c>.
/// </summary>
public static class MemberPathExtensions
{
    extension(Result<IMemberPath> path)
    {
        /// <summary>
        ///     Creates the path to the number of items of the collection at the end of the path.
        /// </summary>
        /// <returns>
        ///     The longer path or the failure of the path or of the step.
        /// </returns>
        public Result<IMemberPath> Count()
        {
            return path.TryUnwrap(out var end, out var error) ? end.Count() : error;
        }

        /// <summary>
        ///     Creates the path to the item of <paramref name="key"/> in the collection at the end of the path.
        /// </summary>
        /// <param name="key">The index or key of the item.</param>
        /// <returns>
        ///     The longer path or the failure of the path or of the step.
        /// </returns>
        public Result<IMemberPath> Item(PathKey key)
        {
            return path.TryUnwrap(out var end, out var error) ? end.Item(key) : error;
        }

        /// <summary>
        ///     Creates the path to the member <paramref name="name"/> of the object or struct at the end of the path.
        /// </summary>
        /// <param name="name">The name of the member.</param>
        /// <returns>
        ///     The longer path or the failure of the path or of the step.
        /// </returns>
        public Result<IMemberPath> Member(string name)
        {
            return path.TryUnwrap(out var end, out var error) ? end.Member(name) : error;
        }

        /// <summary>
        ///     Reads the value at the end of the path once, in the type the engine reads it as.
        /// </summary>
        /// <returns>
        ///     The value or the failure of the path or of the read.
        /// </returns>
        public Result<object?> Read()
        {
            return path.TryUnwrap(out var end, out var error) ? end.Read() : error;
        }

        /// <summary>
        ///     Reads the value of type <typeparamref name="T"/> at the end of the path once.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <returns>
        ///     The value or the failure of the path or of the read.
        /// </returns>
        public Result<T> Read<T>()
        {
            return path.TryUnwrap(out var end, out var error) ? end.Read<T>() : error;
        }

        /// <summary>
        ///     Creates the source of a typed watcher for the value at the end of the path.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <returns>
        ///     The source or the failure of the path or of the step.
        /// </returns>
        public Result<IWatcherSource<T>> Value<T>()
        {
            return path.TryUnwrap(out var end, out var error) ? end.Value<T>() : error;
        }
    }
}
