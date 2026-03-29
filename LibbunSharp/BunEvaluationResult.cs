namespace LibbunSharp;

public readonly record struct BunEvaluationResult(bool Success, string? Error)
{
    public void EnsureSuccess()
    {
        if (!Success)
        {
            throw new BunException(Error ?? "bun evaluation failed.");
        }
    }
}