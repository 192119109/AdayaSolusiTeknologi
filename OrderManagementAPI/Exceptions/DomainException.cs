using System;

namespace OrderManagementAPI.Exceptions
{
    public class DomainException : Exception
    {
        public int StatusCode { get; }

        public DomainException(string message, int statusCode = 400) : base(message)
        {
            StatusCode = statusCode;
        }
    }
}
