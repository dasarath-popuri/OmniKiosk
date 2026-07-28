namespace OmniKiosk.MoneyExchange.Api.Models.v1
{
    public class ScreeningResult
    {
        // true = do not let this transaction proceed on the kiosk; the
        // customer needs to go to the counter.
        public bool HasMatch { get; set; }
    }
}
