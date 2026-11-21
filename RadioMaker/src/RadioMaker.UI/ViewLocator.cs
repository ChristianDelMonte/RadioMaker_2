using Avalonia.Controls;
using Avalonia.Controls.Templates;
using RadioMaker.UI.ViewModels;

namespace RadioMaker.UI;

/// -------------
/// Resuelve automáticamente la vista correspondiente a un ViewModel,
/// buscando un tipo con el mismo nombre pero en el namespace 'Views'.
/// -------------
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null) return null;

        var name = param.GetType().FullName!
            .Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        var type = Type.GetType(name);

        if (type is not null)
            return (Control)Activator.CreateInstance(type)!;

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
