﻿using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace MCPhappey.Servers.SQL.Models;

[PrimaryKey(nameof(Id), nameof(ServerId))]
public class ServerGroup
{
    [Column(Order = 1)]
    public string Id { get; set; } = null!;

    [Column(Order = 2)]
    public int ServerId { get; set; }

    [ForeignKey("ServerId")]
    public Server Server { get; set; } = null!;
}
