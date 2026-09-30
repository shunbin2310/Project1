namespace Project1.Api.Email;

public sealed record RenderedEmailTemplate(
    int TemplateId,
    string TemplateCode,
    int TemplateVersion,
    EmailMessage Message);

public sealed record EmailTemplateRenderResult(
    bool IsSuccess,
    RenderedEmailTemplate? RenderedEmail = null,
    string? ErrorMessage = null);
