using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public record CreateUserRequest
([param: Required, NoControlCharacters]
    [param: MaxLength(100)]
    string FullName,

    [param: Required, NoControlCharacters]
    [param: EmailAddress]
    [param: MaxLength(255)]
    string Email,

    [param: Required, NoControlCharacters]
    [param: MaxLength(20)]
    string Phone,

    [param: Required]
    [param: Range(1, 3)]
    int RoleId,

    [param: MaxLength(200), NoControlCharacters]
    string? CompanyName = null,

    [param: MaxLength(100), NoControlCharacters]
    string? CompanyRegistrationNumber = null,

    /// <summary>
    /// The password of the new account. It is required for a Landlord or an Investor, who
    /// chooses their own password. It is <b>not</b> used for an Admin: a new Admin sets
    /// their own password through the single-use activation link, so a super administrator can
    /// never know or choose it. A value sent for an Admin is ignored.
    /// </summary>
    [param: MaxLength(128), NoControlCharacters, MaxUtf8Bytes(72)]
    string? Password = null);
