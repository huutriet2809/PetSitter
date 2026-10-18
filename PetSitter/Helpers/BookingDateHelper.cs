using System;

namespace PetSitter.Helpers
{
    public static class BookingDateHelper
    {
        /// <summary>
        /// Quy ước tính số ngày dịch vụ: Bao gồm cả hai đầu [NgayBatDau, NgayKetThuc]
        /// Ví dụ: 2026-10-20 đến 2026-10-22 = 3 ngày.
        /// </summary>
        public static int CalculateInclusiveDays(DateTime startDate, DateTime endDate)
        {
            var diff = (endDate.Date - startDate.Date).Days;
            return diff >= 0 ? diff + 1 : 0;
        }

        public static int CalculateInclusiveDays(string startDateStr, string endDateStr)
        {
            if (DateTime.TryParse(startDateStr, out var start) && DateTime.TryParse(endDateStr, out var end))
            {
                return CalculateInclusiveDays(start, end);
            }
            return 0;
        }
    }
}
