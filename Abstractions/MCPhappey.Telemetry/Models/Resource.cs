using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace MCPHappey.Telemetry.Models;

[Index(nameof(Uri), IsUnique = true)]
public class Resource
{
    public int Id { get; set; }

    [MaxLength(850)]  // genoeg ruimte
    public string Uri { get; set; } = null!;

    public ICollection<ResourceRequest> Requests { get; set; } = [];
}
