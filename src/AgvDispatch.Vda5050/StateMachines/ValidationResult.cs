namespace AgvDispatch.Vda5050.StateMachines;

/// <summary>
/// Result of validating sequence or order invariants.
/// </summary>
public class ValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<string> Errors { get; } = new();

    public static ValidationResult Success() => new();

    public static ValidationResult Failure(string error)
    {
        var res = new ValidationResult();
        res.Errors.Add(error);
        return res;
    }

    public static ValidationResult Failure(IEnumerable<string> errors)
    {
        var res = new ValidationResult();
        res.Errors.AddRange(errors);
        return res;
    }

    public void AddError(string error)
    {
        Errors.Add(error);
    }
}
