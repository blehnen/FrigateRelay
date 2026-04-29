using FrigateRelay.Abstractions;
using Microsoft.Extensions.Options;

namespace FrigateRelay.Plugins.Pushover;

/// <summary>
/// Validates <see cref="PushoverOptions"/> beyond DataAnnotations:
/// parses MessageTemplate to enforce the allowed token set at startup.
/// </summary>
internal sealed class PushoverOptionsValidator : IValidateOptions<PushoverOptions>
{
    public ValidateOptionsResult Validate(string? name, PushoverOptions options)
    {
        // DataAnnotations handle AppToken, UserKey (Required), Priority (Range).
        // Here we validate the MessageTemplate token set via EventTokenTemplate.Parse.
        try
        {
            EventTokenTemplate.Parse(options.MessageTemplate, "Pushover.MessageTemplate");
        }
        catch (ArgumentException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }

        return ValidateOptionsResult.Success;
    }
}
