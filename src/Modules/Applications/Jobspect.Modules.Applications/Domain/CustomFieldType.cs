namespace Jobspect.Modules.Applications.Domain;

/// <summary>
/// What kind of value a custom field holds. Fixed at definition time and never
/// changed afterwards: the values are stored against the definition, so turning a
/// text field into a number would invalidate every one already recorded.
/// </summary>
internal enum CustomFieldType
{
    Text,
    Number,
    Date,
    Checkbox,
    SingleSelect,
    MultiSelect,
    Url,
}
