namespace Sms.Application.Services.Auth;

public enum IdentifierKind { Email, Phone, AdmissionId }

public static class IdentifierClassifier
{
    public static IdentifierKind Classify(string identifier)
    {
        if (identifier.Contains('@')) return IdentifierKind.Email;

        var digits = new string(identifier.Where(char.IsDigit).ToArray());
        var hasOnlyPhonePunctuation = identifier.All(c =>
            char.IsDigit(c) || char.IsWhiteSpace(c) || c is '+' or '-' or '(' or ')');

        if (hasOnlyPhonePunctuation && digits.Length is >= 7 and <= 15)
            return IdentifierKind.Phone;

        return IdentifierKind.AdmissionId;
    }
}
