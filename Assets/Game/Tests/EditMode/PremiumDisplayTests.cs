using NeonVelocity.Premium;
using NUnit.Framework;

namespace NeonVelocity.Tests
{
    /// <summary>
    /// Display coverage for server transaction records. The shop renders these labels directly,
    /// so a status or provider change must never surface raw database values to a player.
    /// </summary>
    public sealed class PremiumDisplayTests
    {
        [Test]
        public void StoreTransaction_StatusLabelsArePlayerFacing()
        {
            Assert.AreEqual("PAID", new StoreTransaction { status = "SUCCEEDED" }.StatusLabel);
            Assert.AreEqual("REFUNDED", new StoreTransaction { status = "REFUNDED" }.StatusLabel);
            Assert.AreEqual("PARTIALLY REFUNDED",
                new StoreTransaction { status = "PARTIALLY_REFUNDED" }.StatusLabel);
            Assert.AreEqual("FAILED", new StoreTransaction { status = "FAILED" }.StatusLabel);
            Assert.AreEqual("PENDING", new StoreTransaction { status = "PENDING" }.StatusLabel);
            Assert.AreEqual("UNKNOWN", new StoreTransaction { status = "" }.StatusLabel);
            Assert.AreEqual("CHARGEBACK", new StoreTransaction { status = "CHARGEBACK" }.StatusLabel);
        }

        [Test]
        public void StoreTransaction_AmountLabelFormatsMinorUnits()
        {
            Assert.AreEqual("4.99 USD",
                new StoreTransaction { amountMinor = 499, currency = "USD" }.AmountLabel);
            Assert.AreEqual("1200.00 LKR",
                new StoreTransaction { amountMinor = 120000, currency = "LKR" }.AmountLabel);
            Assert.AreEqual("\u2014", new StoreTransaction { amountMinor = 0, currency = "USD" }.AmountLabel);
            Assert.AreEqual("\u2014", new StoreTransaction { amountMinor = -1, currency = "USD" }.AmountLabel);
        }

        [Test]
        public void StoreTransaction_ProviderLabelsAreReadable()
        {
            Assert.AreEqual("CARD", new StoreTransaction { provider = "stripe" }.ProviderLabel);
            Assert.AreEqual("APP STORE", new StoreTransaction { provider = "apple" }.ProviderLabel);
            Assert.AreEqual("GOOGLE PLAY", new StoreTransaction { provider = "google_play" }.ProviderLabel);
            Assert.AreEqual("STORE", new StoreTransaction { provider = "" }.ProviderLabel);
            Assert.AreEqual("PAYPAL", new StoreTransaction { provider = "paypal" }.ProviderLabel);
        }
    }
}
