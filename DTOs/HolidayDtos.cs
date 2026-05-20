using System.ComponentModel.DataAnnotations;

namespace lms_api.DTOs;

public class CreateHolidayRequest
{
    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public DateTime Date { get; set; }
}

public class UpdateHolidayRequest
{
    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public DateTime Date { get; set; }
}
