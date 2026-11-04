using System.Linq;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PackageGuard.Core.CSharp.FetchingStrategies;

namespace PackageGuard.Specs.CSharp;

[TestClass]
public class LicenseTextClassifierSpecs
{
    [TestMethod]
    public void A_split_license_that_offers_apache_or_a_commercial_license_is_not_reported_as_apache()
    {
        // Arrange
        const string text = """
            Six Labors Split License
            Version 1.0, June 2022

            Works in Source or Object form are split licensed and may be licensed under the Apache License, Version 2.0
            or a Six Labors Commercial Use License.
            """;

        // Act
        string license = LicenseTextClassifier.Identify(text);

        // Assert
        license.Should().BeNull();
    }

    [TestMethod]
    public void A_dual_licensed_text_mentioning_apache_is_not_reported_as_apache()
    {
        // Act
        string license = LicenseTextClassifier.Identify(
            "Apache License\nVersion 2.0, January 2004\n\nThis work is dual-licensed under Apache or a commercial license.");

        // Assert
        license.Should().BeNull();
    }

    [TestMethod]
    public void A_regular_apache_license_is_recognized()
    {
        // Act
        string license = LicenseTextClassifier.Identify("                                 Apache License\n" +
            "                           Version 2.0, January 2004\n                        http://www.apache.org/licenses/");

        // Assert
        license.Should().Be("Apache-2.0");
    }

    [TestMethod]
    public void A_regular_mit_license_is_recognized()
    {
        // Act
        string license = LicenseTextClassifier.Identify(
            "MIT License\n\nCopyright (c) 2024 Someone\n\nPermission is hereby granted, free of charge, to any person");

        // Assert
        license.Should().Be("MIT");
    }

    [TestMethod]
    public void A_mit_license_that_is_only_mentioned_in_the_body_is_not_recognized()
    {
        // Arrange
        string text = "Proprietary License Agreement\n" + string.Join('\n', Enumerable.Repeat("All rights reserved.", 20)) +
            "\nThird-party notices: this product includes software under the MIT license.";

        // Act
        string license = LicenseTextClassifier.Identify(text);

        // Assert
        license.Should().BeNull();
    }

    [TestMethod]
    public void A_text_that_matches_multiple_licenses_is_not_recognized()
    {
        // Act
        string license = LicenseTextClassifier.Identify("MIT License\nor the Apache License\nVersion 2.0");

        // Assert
        license.Should().BeNull();
    }

    [TestMethod]
    [DataRow("GNU GENERAL PUBLIC LICENSE\nVersion 2, June 1991", "GPL-2.0")]
    [DataRow("GNU GENERAL PUBLIC LICENSE\nVersion 3, 29 June 2007", "GPL-3.0")]
    [DataRow("GNU LESSER GENERAL PUBLIC LICENSE\nVersion 3, 29 June 2007\n\nrefers to the GNU General Public License",
        "LGPL-3.0")]
    [DataRow("GNU LESSER GENERAL PUBLIC LICENSE\nVersion 2.1, February 1999", "LGPL-2.1")]
    [DataRow("GNU AFFERO GENERAL PUBLIC LICENSE\nVersion 3, 19 November 2007", "AGPL-3.0")]
    public void The_gnu_family_of_licenses_is_distinguished_by_name_and_version(string text, string expected)
    {
        // Act
        string license = LicenseTextClassifier.Identify(text);

        // Assert
        license.Should().Be(expected);
    }

    [TestMethod]
    public void The_license_text_is_extracted_from_the_nuget_license_page()
    {
        // Arrange
        const string page = "<!DOCTYPE html><html><head><title>NuGet Gallery | License of Some.Package 1.0.0</title></head>\n" +
            "<body><nav>Packages</nav><nav>Upload</nav><nav>Statistics</nav><nav>Documentation</nav>\n" +
            "<pre class=\"license-file-contents custom-license-container\">MICROSOFT SOFTWARE LICENSE TERMS\n" +
            "MICROSOFT .NET LIBRARY</pre></body></html>";

        // Act
        string license = LicenseTextClassifier.Identify(page);

        // Assert
        license.Should().Be("Microsoft .NET Library License");
    }

    [TestMethod]
    public void A_license_in_a_plain_html_page_is_recognized_without_the_markup()
    {
        // Act
        string license = LicenseTextClassifier.Identify("<html><head><title>x</title></head><body><h1>MIT License</h1>" +
            "<p>Permission is hereby granted, free of charge</p></body></html>");

        // Assert
        license.Should().Be("MIT");
    }

    [TestMethod]
    public void The_full_gpl_2_text_is_recognized_even_though_its_preamble_mentions_the_lgpl()
    {
        // Arrange
        string text = string.Join('\n',
            "                    GNU GENERAL PUBLIC LICENSE",
            "                       Version 2, June 1991",
            "",
            " Copyright (C) 1989, 1991 Free Software Foundation, Inc.,",
            " 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA",
            " Everyone is permitted to copy and distribute verbatim copies",
            " of this license document, but changing it is not allowed.",
            "",
            "                            Preamble",
            "",
            "  The licenses for most software are designed to take away your",
            "freedom to share and change it.  By contrast, the GNU General Public",
            "License is intended to guarantee your freedom to share and change free",
            "software.  This license applies to most of the Free Software",
            "Foundation's software.  Some other FSF software is covered by",
            "the GNU Lesser General Public License instead.");

        // Act
        string license = LicenseTextClassifier.Identify(text);

        // Assert
        license.Should().Be("GPL-2.0");
    }

    [TestMethod]
    [DataRow("The JSON License\n\nPermission is hereby granted, free of charge, to any person obtaining a copy\n" +
        "The Software shall be used for Good, not Evil.")]
    [DataRow("Boost Software License - Version 1.0 - August 17th, 2003\n\nPermission is hereby granted, free of charge, to any")]
    [DataRow("Copyright (c) 2024 Someone\n\nPermission is hereby granted, free of charge, to any person obtaining a copy")]
    public void A_license_that_only_shares_the_permission_sentence_with_mit_is_not_reported_as_mit(string text)
    {
        // Act
        string license = LicenseTextClassifier.Identify(text);

        // Assert
        license.Should().BeNull();
    }

    [TestMethod]
    public void An_apache_license_that_is_named_in_one_line_is_recognized()
    {
        // Act
        string license = LicenseTextClassifier.Identify("Copyright 2020 Contoso\n\nLicensed under the Apache License 2.0.");

        // Assert
        license.Should().Be("Apache-2.0");
    }

    [TestMethod]
    public void The_license_on_a_web_page_is_found_through_its_title_even_when_the_page_has_a_lot_of_navigation()
    {
        // Arrange
        string navigation = string.Concat(Enumerable.Repeat("<li><a href=\"/x\">Menu item</a></li>\n", 40));
        string page = "<!DOCTYPE html><html><head><title>The MIT License &#8211; Open Source Initiative</title></head><body>" +
            "<ul>" + navigation + "</ul><h1>The MIT License</h1><p>Permission is hereby granted, free of charge</p>" +
            "</body></html>";

        // Act
        string license = LicenseTextClassifier.Identify(page);

        // Assert
        license.Should().Be("MIT");
    }

    [TestMethod]
    public void A_minified_web_page_that_only_mentions_mit_in_its_body_is_not_recognized()
    {
        // Arrange
        const string page = "<html><head><title>Contoso</title></head><body><h1>Contoso Proprietary License Agreement</h1>" +
            "<p>All rights reserved.</p><p>One.</p><p>Two.</p><p>Three.</p><p>Four.</p><p>Five.</p><p>Six.</p><p>Seven.</p>" +
            "<p>Eight.</p><p>Nine.</p><p>Ten.</p><p>Eleven.</p><p>Twelve.</p><p>Third-party notices: this product includes " +
            "software under the MIT license.</p></body></html>";

        // Act
        string license = LicenseTextClassifier.Identify(page);

        // Assert
        license.Should().BeNull();
    }

    [TestMethod]
    public void The_microsoft_net_library_license_is_recognized()
    {
        // Act
        string license = LicenseTextClassifier.Identify("MICROSOFT SOFTWARE LICENSE TERMS\nMICROSOFT .NET LIBRARY");

        // Assert
        license.Should().Be("Microsoft .NET Library License");
    }
}
