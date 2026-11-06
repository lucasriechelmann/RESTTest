using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>An earlier response the current one is compared with, and when it was received.</summary>
/// <param name="Pinned">True when the user chose it as the baseline; false when it is simply the previous response.</param>
public sealed record ResponseBaseline(HttpExchangeResult Result, DateTime When, bool Pinned);
