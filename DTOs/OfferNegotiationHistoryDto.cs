using NibrasWeb.Enums;

namespace NibrasWeb.DTOs
{
    public class OfferNegotiationHistoryDto
    {
        public int Id { get; set; }
        public int OfferId { get; set; }
        public OfferHistoryActionType ActionType { get; set; }
        public int ActorId { get; set; }
        public string? ActorName { get; set; }
        public string? ActorRole { get; set; }
        public decimal? Amount { get; set; }
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
