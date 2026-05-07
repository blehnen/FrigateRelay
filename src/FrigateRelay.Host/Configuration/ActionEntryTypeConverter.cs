using System.ComponentModel;
using System.Globalization;

namespace FrigateRelay.Host.Configuration;

/// <summary>
/// Allows <c>IConfiguration.Bind</c> to convert a scalar string value
/// (e.g. <c>"BlueIris"</c>) in an <c>Actions</c> array into an
/// <see cref="ActionEntry"/> with only <see cref="ActionEntry.Plugin"/> set.
///
/// <para>
/// This closes ID-12: without this converter, <c>ConfigurationBinder</c> calls
/// <c>TypeDescriptor.GetConverter(typeof(ActionEntry))</c> for each scalar element and
/// finds no converter — the element is silently skipped, producing an empty list at
/// runtime. Registering this converter via <c>[TypeConverter]</c> on the type causes
/// the binder to invoke <see cref="ConvertFrom"/> for scalar strings.
/// </para>
///
/// <para>
/// Object-form elements (e.g. <c>{"Plugin":"Pushover","SnapshotProvider":"Frigate"}</c>)
/// are not routed through this converter — the binder maps them property-by-property
/// using reflection. Both paths coexist without interference.
/// </para>
/// </summary>
internal sealed class ActionEntryTypeConverter : TypeConverter
{
    /// <inheritdoc/>
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc/>
    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        // Note: string shorthand "BlueIris" → ActionEntry("BlueIris"); ParallelValidators defaults to false.
        // No change needed here when ActionEntry gains new optional fields — default values handle back-compat.
        // Object-form entries ({"Plugin":"X","ParallelValidators":true}) are NOT routed through this converter;
        // IConfiguration.Bind maps them property-by-property via reflection (see ActionEntryJsonConverter for
        // the JSON path — the two converters operate on disjoint code paths).
        if (value is string s)
        {
            // #14: reject empty/whitespace names at the converter boundary.
            // IsNullOrWhiteSpace is stricter than the JSON path's IsNullOrEmpty on purpose —
            // IConfiguration.Bind can hand us a whitespace-only string from blank config values.
            if (string.IsNullOrWhiteSpace(s))
                throw new FormatException($"ActionEntry plugin name cannot be empty or whitespace (received: '{s}').");

            return new ActionEntry(s);
        }

        return base.ConvertFrom(context, culture, value)!;
    }
}
