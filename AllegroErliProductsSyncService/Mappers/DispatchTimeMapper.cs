using AllegroErliProductsSyncService.DTOs;
using System;
using System.Text.RegularExpressions;

namespace AllegroErliProductsSyncService.Mappers
{
    public static class DispatchTimeMapper
    {
        public static ErliDispatchTime MapFromHandlingTime(string handlingTime)
        {
            if (string.IsNullOrWhiteSpace(handlingTime))
                return new ErliDispatchTime { Period = 1 }; // default 1 day if missing

            // Normalize casing (PT24H -> PT24H, pt3d -> PT3D, etc.)
            handlingTime = handlingTime.Trim().ToUpperInvariant();

            // Match patterns like PT24H, PT48H, P3D, etc.
            var match = Regex.Match(handlingTime, @"P(T?(?<hours>\d+)H)?(?<days>\d+)D?");

            int days = 0;

            if (match.Success)
            {
                // Check for days (P3D)
                if (match.Groups["days"].Success)
                    days = int.Parse(match.Groups["days"].Value);

                // Check for hours (PT24H)
                if (match.Groups["hours"].Success)
                {
                    var hours = int.Parse(match.Groups["hours"].Value);
                    days += (int)Math.Ceiling(hours / 24.0);
                }
            }
            else
            {
                // Fallback for simple patterns like "PT72H", "PT48H", "PT24H"
                var hoursMatch = Regex.Match(handlingTime, @"PT(?<hours>\d+)H");
                if (hoursMatch.Success)
                {
                    var hours = int.Parse(hoursMatch.Groups["hours"].Value);
                    days = (int)Math.Ceiling(hours / 24.0);
                }
            }

            // Default to 1 day minimum
            if (days < 1)
                days = 1;

            return new ErliDispatchTime { Period = days };
        }
    }
}