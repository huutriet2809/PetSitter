using System;
using System.Collections.Generic;

namespace PetSitter.DTOs
{
    public class StayUpdateDto
    {
        public string Id { get; set; } = string.Empty;
        public string BookingId { get; set; } = string.Empty;
        public string? Note { get; set; }
        public List<string> PhotoPaths { get; set; } = new List<string>();
        public DateTime CreatedAt { get; set; }
        public bool CanDelete { get; set; }
    }
}