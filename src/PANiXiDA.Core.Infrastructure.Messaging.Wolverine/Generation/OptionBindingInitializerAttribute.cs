using System.ComponentModel;

namespace PANiXiDA.Core.Infrastructure.Messaging.Wolverine.Generation;

/// <summary>
/// Identifies generated binder registration that can run before module initialization completes.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class OptionBindingInitializerAttribute : Attribute
{
    /// <summary>
    /// Identifies the options type handled by this generated registration.
    /// </summary>
    /// <param name="optionType">The concrete options type.</param>
    protected OptionBindingInitializerAttribute(Type optionType)
    {
        ArgumentNullException.ThrowIfNull(optionType);
        OptionType = optionType;
    }

    /// <summary>
    /// Gets the options type handled by this generated registration.
    /// </summary>
    public Type OptionType { get; }

    /// <summary>
    /// Registers the statically compiled binders provided by the declaring assembly.
    /// </summary>
    public abstract void RegisterBindings();
}
