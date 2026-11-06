namespace RESTTest.Models;

/// <summary>
/// How a request's body is built and which Content-Type it is sent with. <see cref="Json"/> is the
/// default so collections saved before this existed (which only had a JSON body) behave unchanged.
/// </summary>
public enum BodyKind
{
    /// <summary>The <see cref="RequestModel.Body"/> text, as application/json.</summary>
    Json = 0,

    /// <summary>The <see cref="RequestModel.Body"/> text, as text/plain.</summary>
    Text = 1,

    /// <summary>The <see cref="RequestModel.Body"/> text, as application/xml.</summary>
    Xml = 2,

    /// <summary>The <see cref="RequestModel.FormFields"/>, as application/x-www-form-urlencoded.</summary>
    FormUrlEncoded = 3,

    /// <summary>The <see cref="RequestModel.MultipartParts"/>, as multipart/form-data (text and files).</summary>
    Multipart = 4,

    /// <summary>Nothing is sent. Any typed body text is kept, so switching back does not lose it.</summary>
    None = 5,
}
