namespace PDFEmailServiceUFMS.Configuration;

public class EmailSettings
{
    public string SmtpServer { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public bool UseDefaultCredentials { get; set; } = false;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    /// <summary>Subject when BOTH certificates are attached. {0} = REG_BK/REG_BR/REG_NO.</summary>
    public string SubjectTemplate { get; set; } = "Income Tax & Investment Certificate of Registration No. ( {0} )";

    /// <summary>Subject when only the Income Tax Certificate is attached. {0} = REG_BK/REG_BR/REG_NO.</summary>
    public string SubjectTemplateTaxOnly { get; set; } = "Income Tax Certificate of Registration No. ( {0} )";

    /// <summary>Subject when only the Investment Certificate is attached. {0} = REG_BK/REG_BR/REG_NO.</summary>
    public string SubjectTemplateInvestmentOnly { get; set; } = "Investment Certificate of Registration No. ( {0} )";
    public string BodyTemplate { get; set; } = "Your Income Tax Certificate and Investment Certificate (where applicable) are attached. Please review them, and if you find any errors, contact the Unit Fund Department.";
}
