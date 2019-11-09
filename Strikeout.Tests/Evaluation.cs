using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;

using static Strikeout.Tests.Properties.Resources;

namespace Strikeout.Tests
{
    [TestClass]
    public class Evaluation
    {
        // TODO: Test functionality when dependencies that include a custom target fail.

        static Analyzer<object> Essential { get; set; } = new Analyzer<object>
        {
            Key = nameof(Essential),
            Metadata = Analyzer.Characteristics.Generic,
            Processor = container => container.Data switch
            {
                null => EssentialFailureA,
                _ => true
            }
        };

        static Analyzer<string> Basic { get; set; } = new Analyzer<string>
        {
            Key = nameof(Basic),
            Dependencies = { Essential },
            Metadata = Analyzer.Characteristics.Generic,
            Processor = container => container.Data switch
            {
                "" => BasicFailureA,
                { Length: 1 } => BasicFailureB,
                _ => true
            }
        };

        static Analyzer<string> Arbitrary { get; set; } = new Analyzer<string>
        {
            Key = nameof(Arbitrary),
            Dependencies = { Basic },
            Metadata = Analyzer.Characteristics.Generic,
            Processor = container => container.Data switch
            {
                "Peter" => ArbitraryFailureA,
                "Potato" => ArbitraryFailureB,
                _ => true
            }
        };

        static Analyzer<string> Example { get; set; } = new Analyzer<string>
        {
            Key = nameof(Example),
            Dependencies = { Arbitrary, Arbitrary.Depend("Blah") },
            Metadata = Analyzer.Characteristics.Generic,
            Processor = container => container.Data switch
            {
                { Length: 13 } => ExampleFailureA,
                { Length: int length } when length >= 15 => $"{{0}} can't be {length} characters long!", // NOTE: Currently untestable.
                _ => true
            }
        };
        
        static Analyzer<string> Auxiliary { get; set; } = new Analyzer<string>
        {
            Key = nameof(Auxiliary),
            Dependencies = { Arbitrary, Arbitrary.Depend("Boof") },
            Metadata = Analyzer.Characteristics.Generic,
            Processor = container => container.Data switch
            {
                { Length: 3 } => AuxiliaryFailureA,
                { Length: int length } when length >= 16 => $"{{0}} can't be {length} characters long!", // Currently untestable.
                _ => true
            }
        };
        
        static Analyzer<string> Everything { get; set; } = new Analyzer<string>
        {
            Key = nameof(Everything),
            Dependencies = { Auxiliary, Example },
            Metadata = Analyzer.Characteristics.Generic,
            Processor = container => true
        };

        [DataTestMethod, DataRow("Toad", "Foot", DisplayName = "Dataset 1"), DataRow("Frog", "Hand", DisplayName = "Dataset 2")]
        public void TestUnkeyedPackageAnalysisAgainstValidData(string primary, string secondary)
        {
            Analysis analysis = Analysis.Create(Example.Package(primary), Auxiliary.Package(secondary));

            Assert.IsNotNull(analysis);

            Outcome target = analysis[Example.Key], auxiliary = analysis[Auxiliary.Key];

            Assert.IsNotNull(target);
            Assert.IsNotNull(auxiliary);

            Assert.IsTrue(target.Positive);
            Assert.IsTrue(auxiliary.Positive);

            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Basic.Key]);
            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Arbitrary.Key]);
            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Everything.Key]);
        }
        
        [DataTestMethod, DataRow("Toad", "Doesn't Matter", DisplayName = "Datapoint 1"), DataRow("Frog", "Really Shouldn't Matter", DisplayName = "Datapoint 2")]
        public void TestKeyedPackageAnalysisAgainstValidData(string data, string key)
        {
            Analysis analysis = Analysis.Create(Example.Package(data, key));

            Assert.IsNotNull(analysis);

            Outcome target = analysis[key];

            Assert.IsNotNull(target);

            Assert.IsTrue(target.Positive);

            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Basic.Key]);
            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Arbitrary.Key]);
            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Example.Key]);
            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Auxiliary.Key]);
            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Everything.Key]);
        }

        [DataTestMethod, DataRow(default, nameof(EssentialFailureA), DisplayName = "Essential Failure Case 1"), DataRow("", nameof(BasicFailureA), DisplayName = "Basic Failure Case 1"), DataRow(" ", nameof(BasicFailureB), DisplayName = "Basic Failure Case 2"), DataRow("Peter", nameof(ArbitraryFailureA), DisplayName = "Arbitrary Failure Case 1"), DataRow("Potato", nameof(ArbitraryFailureB), DisplayName = "Arbitrary Failure Case 2"), DataRow("1234567890123", nameof(ExampleFailureA), DisplayName = "Example Failure Case 1"), DataRow("123", nameof(AuxiliaryFailureA), DisplayName = "Auxiliary Failure Case 1")]
        public void TestUnkeyedPackageAnalysisAgainstInvalidData(string data, string resource)
        {
            // NOTE: The Example analyzer is being treated as a reference here, as in validation of the package being made from it should always succeed.

            Analysis analysis = Analysis.Create(Everything.Package(data), Example.Package("Valid"));

            Assert.IsNotNull(analysis);

            Outcome target = analysis[Everything.Key], reference = analysis[Example.Key];

            Assert.IsNotNull(target);
            Assert.IsNotNull(reference);

            Assert.IsTrue(reference.Positive);
            Assert.IsFalse(target.Positive);

            Assert.AreEqual(target.Message, ResourceManager.GetString(resource).Replace("{0}", Everything.Key));

            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Basic.Key]);
            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Arbitrary.Key]);
            Assert.ThrowsException<KeyNotFoundException>(() => analysis[Auxiliary.Key]);
        }
    }
}
