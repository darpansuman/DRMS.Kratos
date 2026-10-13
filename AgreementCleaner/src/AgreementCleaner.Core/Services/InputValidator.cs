using AgreementCleaner.Core.Models;

namespace AgreementCleaner.Core.Services;

/// <summary>The result of checking the search fields.</summary>
public sealed class ValidationResult
{
    public List<string> Errors { get; } = new();
    public AgreementSearch? Search { get; set; }
    public bool IsValid => Errors.Count == 0 && Search is not null;
}

/// <summary>
/// Checks and cleans up the values typed into the search fields.
/// </summary>
public static class InputValidator
{
    public const int MinImeiLength = 14;
    public const int MaxImeiLength = 16;
    public const int MinPhoneLength = 7;
    public const int MaxPhoneLength = 15;

    public static ValidationResult Validate(string? agreementId, string? customerId, string? imei, string? phoneNumber)
    {
        var result = new ValidationResult();

        // Agreement Id: whole number only.
        string? cleanAgreementId = null;
        if (!string.IsNullOrWhiteSpace(agreementId))
        {
            var value = agreementId.Trim();
            if (!value.All(char.IsAsciiDigit) || !long.TryParse(value, out var number) || number <= 0)
                result.Errors.Add("Agreement Id must be a whole number, for example 713.");
            else
                cleanAgreementId = number.ToString();
        }

        // Customer Id: a GUID, with or without { } around it.
        Guid? cleanCustomerId = null;
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            if (Guid.TryParse(customerId.Trim(), out var guid))
                cleanCustomerId = guid;
            else
                result.Errors.Add("Customer Id must be a GUID, for example F2246EE9-46D9-8560-9E2A-46257ACD17B0.");
        }

        // IMEI: digits only, normally 15 long.
        string? cleanImei = null;
        if (!string.IsNullOrWhiteSpace(imei))
        {
            var value = imei.Trim();
            if (!value.All(char.IsAsciiDigit) || value.Length < MinImeiLength || value.Length > MaxImeiLength)
                result.Errors.Add($"IMEI must contain only digits and be {MinImeiLength} to {MaxImeiLength} digits long.");
            else
                cleanImei = value;
        }

        // Phone number: allow spaces, dashes, brackets and a leading +, then keep only the digits.
        string? cleanPhone = null;
        if (!string.IsNullOrWhiteSpace(phoneNumber))
        {
            var value = phoneNumber.Trim();
            if (value.StartsWith('+'))
                value = value[1..];
            value = new string(value.Where(c => c is not (' ' or '-' or '(' or ')')).ToArray());

            if (!value.All(char.IsAsciiDigit) || value.Length < MinPhoneLength || value.Length > MaxPhoneLength)
                result.Errors.Add($"Phone Number must contain {MinPhoneLength} to {MaxPhoneLength} digits (spaces, dashes and a leading + are allowed).");
            else
                cleanPhone = value;
        }

        if (result.Errors.Count == 0 &&
            cleanAgreementId is null && cleanCustomerId is null && cleanImei is null && cleanPhone is null)
        {
            result.Errors.Add("Fill in at least one field: Agreement Id, Customer Id, IMEI or Phone Number.");
        }

        if (result.Errors.Count == 0)
            result.Search = new AgreementSearch(cleanAgreementId, cleanCustomerId, cleanImei, cleanPhone);

        return result;
    }
}
