namespace NibrasWeb.Enums
{
    public enum OfferStatus
    {
        PendingLandlordResponse = 1,
        PendingInvestorResponse = 2,
        PendingAdminApproval = 3,
        Accepted = 4,
        RejectedByLandlord = 5,
        RejectedByInvestor = 6,
        RejectedByAdmin = 7,
        WithdrawnByInvestor = 8,
        Closed = 9
    }
}
