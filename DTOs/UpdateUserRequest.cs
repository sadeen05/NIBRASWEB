using System.ComponentModel.DataAnnotations;

namespace NIBRAS.API.DTOs;

public record UpdateUserRequest
(
    [param: Required, MaxLength(100), NoControlCharacters]
    string FullName,

    [param: Required, EmailAddress, MaxLength(255), NoControlCharacters]
    string Email,

    [param: Required, MaxLength(20), NoControlCharacters]
    string Phone
);
