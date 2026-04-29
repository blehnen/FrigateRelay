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
        => value is string s
            ? new ActionEntry(s)
            : base.ConvertFrom(context, culture, value)!;
}
