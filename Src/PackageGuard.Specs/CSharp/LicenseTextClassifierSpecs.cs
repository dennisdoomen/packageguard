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
    public void The_microsoft_net_library_license_is_recognized()
    {
        // Act
        string license = LicenseTextClassifier.Identify("MICROSOFT SOFTWARE LICENSE TERMS\nMICROSOFT .NET LIBRARY");

        // Assert
        license.Should().Be("Microsoft .NET Library License");
    }
}
