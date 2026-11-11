using Avalonia.Data.Converters;
using Ps2Ulix.Core;

namespace Ps2Ulix.Gui;

public static class Converters
{
    public static readonly IValueConverter IsError = new FuncValueConverter<IssueSeverity, bool>(s => s == IssueSeverity.Error);
}
