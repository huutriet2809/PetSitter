using System;

namespace PetSitter.Exceptions
{
    public abstract class AppException : Exception
    {
        public int StatusCode { get; }

        protected AppException(string message, int statusCode) : base(message)
        {
            StatusCode = statusCode;
        }
    }

    public class BadRequestException : AppException
    {
        public BadRequestException(string message) : base(message, 400) { }
    }

    public class ForbiddenException : AppException
    {
        public ForbiddenException(string message) : base(message, 403) { }
    }

    public class NotFoundException : AppException
    {
        public NotFoundException(string message) : base(message, 404) { }
    }

    public class ConflictException : AppException
    {
        public ConflictException(string message) : base(message, 409) { }
    }

    public class PriceMismatchException : AppException
    {
        public long NewTotal { get; }
        public PriceMismatchException(long expectedTotal, long newTotal) 
            : base($"Giá dịch vụ đã thay đổi hoặc không khớp. Giá bạn gửi: {expectedTotal:N0} ₫, giá mới nhất: {newTotal:N0} ₫. Vui lòng cập nhật báo giá.", 409)
        {
            NewTotal = newTotal;
        }
    }
}

