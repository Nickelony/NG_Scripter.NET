namespace TRNGScriptCompiler.Model;

/// <summary>Holds one parsed NG command argument: its type and its number, text, boolean or array value.</summary>
/// <remarks>
/// The value never changes after creation: an array argument holds its own copy of the elements.
/// </remarks>
internal readonly record struct NgArgumentValue
{
	private readonly int _number;
	private readonly string? _text;
	private readonly NgArgumentValue[]? _items;

	private NgArgumentValue(NgArgumentType type, int number, string? text, NgArgumentValue[]? items)
	{
		Type = type;
		_number = number;
		_text = text;
		_items = items;
	}

	internal NgArgumentType Type { get; }

	/// <exception cref="InvalidOperationException"><see cref="Type"/> is not a numeric type.</exception>
	internal int Int32Value =>
		Type switch
		{
			NgArgumentType.Word or NgArgumentType.Integer or NgArgumentType.Long or NgArgumentType.ItemSlot or NgArgumentType.Import => _number,
			_ => throw new InvalidOperationException($"Argument is not a numeric type (actual: {Type})."),
		};

	/// <exception cref="InvalidOperationException">
	/// <see cref="Type"/> is not <see cref="NgArgumentType.String"/>.
	/// </exception>
	internal string StringValue =>
		Type == NgArgumentType.String && _text is not null
			? _text
			: throw new InvalidOperationException($"Argument is not a String type (actual: {Type}).");

	/// <exception cref="InvalidOperationException">
	/// <see cref="Type"/> is not <see cref="NgArgumentType.Bool"/>.
	/// </exception>
	internal bool BoolValue =>
		Type == NgArgumentType.Bool ? _number != 0 : throw new InvalidOperationException($"Argument is not a Bool type (actual: {Type}).");

	/// <summary>
	/// Gets the elements of an array argument, or <see langword="null"/> when <see cref="Type"/> is not an array type.
	/// </summary>
	internal IReadOnlyList<NgArgumentValue>? ArrayValue => _items;

	/// <summary>Gets a value indicating whether this argument represents a discard/ignore sentinel.</summary>
	/// <remarks>
	/// For strings, the sentinel is the <c>"*"</c> wildcard; for numeric types, the value <c>-1</c>.
	/// </remarks>
	internal bool IsDiscard => Type == NgArgumentType.String ? _text == "*" : Type != NgArgumentType.Bool && _items is null && _number == -1;

	/// <summary>
	/// Creates an argument value for a 32-bit integer (covers Word, Integer, Long, ItemSlot, Import).
	/// </summary>
	internal static NgArgumentValue ForInt32(NgArgumentType type, int value) => new(type, number: value, text: null, items: null);

	internal static NgArgumentValue ForString(string value) => new(type: NgArgumentType.String, number: 0, text: value, items: null);

	/// <summary>Creates an argument value for a boolean (ENABLED/DISABLED).</summary>
	internal static NgArgumentValue ForBool(bool value) => new(type: NgArgumentType.Bool, number: value ? 1 : 0, text: null, items: null);

	/// <summary>Creates an argument value for an array type from a copy of the elements.</summary>
	internal static NgArgumentValue ForArray(NgArgumentType type, IEnumerable<NgArgumentValue> items) =>
		new(type, number: 0, text: null, items: [.. items]);
}
