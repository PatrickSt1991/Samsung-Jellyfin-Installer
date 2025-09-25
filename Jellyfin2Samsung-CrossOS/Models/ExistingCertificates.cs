using System;
using System.Globalization;

namespace Jellyfin2SamsungCrossOS.Models
{
    public class ExistingCertificates
    {
        public required string Name { get; set; }
        public string? File { get; set; }
        public DateTime? ExpireDate { get; set; }
        public bool? Expired => ExpireDate.HasValue ? ExpireDate.Value < DateTime.Now : null;
        public string? Status { get; set; }
        public string DisplayText =>
            $"{Name}" + (
                Status != null ? $" ({Status})" :
                ExpireDate.HasValue ? $" ({ExpireDate.Value.ToString("D", CultureInfo.CurrentCulture)})" : ""
            );
    }
}
