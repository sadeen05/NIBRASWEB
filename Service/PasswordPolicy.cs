using System.Text;
using NIBRAS;

namespace NIBRAS.API.Services;

public static class PasswordPolicy
{
    public static int GetUtf8ByteCount(string value)
    {
        var byteCount = 0;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character <= 0x7F)
            {
                byteCount++;
            }
            else if (character <= 0x7FF)
            {
                byteCount += 2;
            }
            else if (char.IsHighSurrogate(character) &&
                     index + 1 < value.Length &&
                     char.IsLowSurrogate(value[index + 1]))
            {
                byteCount += 4;
                index++;
            }
            else
            {
                byteCount += 3;
            }
        }

        return byteCount;
    }

    public static void Validate(string password)
    {
        if (GetUtf8ByteCount(password) > 72)
            throw new ValidationFailedException("Password exceeds the supported length.");
    }
}
