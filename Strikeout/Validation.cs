using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace Strikeout
{
    // NOTE: Interfaces for base classes may become useful in future API iterations and do not cost anything, so they are being kept around.

    public interface IOutcome
    {
        string Key { get; }

        string Message { get; }
    }
    
    public interface IOutcome<TData> : IOutcome
    {
        TData Data { get; }
    }

    public class Outcome : IOutcome
    {
        [Flags]
        public enum Characteristics
        {
            None = 0,
            Successful = 1 << 0
        }

        public string Key { get; set; }

        public string Message { get; set; }

        public Characteristics Metadata { get; set; }

        public bool Positive
        {
            get => Metadata.HasFlag(Characteristics.Successful);
            set => Metadata |= value ? Characteristics.Successful : 0;
        }

        public static implicit operator Outcome(string message) => new Outcome { Message = message };

        public static implicit operator Outcome(bool successful) => new Outcome { Metadata = successful ? Characteristics.Successful : Characteristics.None };

        public Outcome Identify(string key, bool generic = false)
        {
            Key = key;
            if (generic)
            {
                try
                {
                    Message = String.Format(CultureInfo.InvariantCulture, Message, Key);
                }
                catch { };
            }
            return this;
        }
    }

    public class Outcome<TData> : Outcome, IOutcome<TData>
    {
        public TData Data { get; set; }
    }

    public interface IPackage
    {
        string Key { get; }

        Outcome[] Evaluation { get; }
    }

    public interface IDependency
    {
        public Outcome Process<TContainerData>(Container<TContainerData> container);
    }

    public abstract class Dependency : IDependency
    {
        public abstract Outcome Process<TContainerData>(Container<TContainerData> container);
    }

    public class Dependency<TData> : Dependency
    {
        [Flags]
        public enum Characteristics
        {
            None,
            Halting = 1 << 0,
        }

        public Characteristics Metadata { get; set; } = Characteristics.Halting;

        public Analyzer<TData> Analyzer { get; set; }

        public TData Data { get; set; }

        public override Outcome Process<TContainerData>(Container<TContainerData> container)
        {
            if (Data is { })
            {
                return Analyze(new Container<TData> { Data = Data }, true);
            }
            else if (typeof(TData) == typeof(TContainerData))
            {
                return Analyze(container as Container<TData>);
            }
            else if (typeof(TContainerData).IsSubclassOf(typeof(TData)))
            {
                // NOTE: This performs an effective cast from container to Container<TData> when TData is not exactly TContainerData, but it is dynamically known that TContainerData derives from TData.
                
                Container<TData> proxy = new Container<TData> 
                { 
                    Data = (TData)Convert.ChangeType(container.Data, typeof(TData), CultureInfo.InvariantCulture),
                    Head = false
                };

                proxy.Results.AddRange(container.Results);
                return Analyze(proxy);
            }
            else throw new InvalidOperationException("A dependency was specified on an analyzer with a different target data type, and no alternate target instance was provided to analyze.");

            Outcome Analyze(Container<TData> target, bool tangent = false)
            {
                Outcome outcome = Analyzer.Process(target);

                if (!outcome.Metadata.HasFlag(Outcome.Characteristics.Successful) && Metadata.HasFlag(Characteristics.Halting)) return new Outcome<Outcome> { Key = "Dependency Process Result", Data = outcome, Message = $"This Dependency<{typeof(TData).Name}>, with \"{Data?.ToString()}\" as attached data, failed to process {(tangent ? "a tangential analysis" : $"the container-provided data \"{target.Data?.ToString()}\"")} using the analyzer with key \"{Analyzer?.Key}\"." };
                else if (tangent) container.Results.AddRange(target.Results);
                else container.Results.Add(outcome);

                return true;
            }
        }

        public static implicit operator Dependency<TData>(Analyzer<TData> analyzer) => new Dependency<TData> { Analyzer = analyzer };

        public static implicit operator Dependency<TData>((Analyzer<TData> Analyzer, TData Data) values) => new Dependency<TData> { Analyzer = values.Analyzer, Data = values.Data };
    }

    public interface IContainer
    {
        public bool Head { get; }

        List<Outcome> Results { get; }

        public Outcome<TData> GetOutcome<TData>(string key = default) => Results.FirstOrDefault(outcome => (key is null || outcome.Key == key) && outcome is Outcome<TData>) as Outcome<TData>;
    }

    public class Container<TData> : IContainer
    {
        public bool Head { get; set; } = true;

        public List<Outcome> Results { get; } = new List<Outcome> { };

        public TData Data { get; set; }
    }

    public interface IAnalyzer
    {
        // TODO: Look into modifying this and requiring it in Analyzer implementations.

        Outcome Process(IContainer container);
    }

    public abstract class Analyzer
    {
        [Flags]
        public enum Characteristics
        {
            None,
            Generic = 1 << 0
        }

        public Characteristics Metadata { get; set; }

        public string Key { get; set; }

        public List<Dependency> Dependencies { get; set; } = new List<Dependency> { };
    }

    public class Analyzer<TData> : Analyzer
    {
        // TODO: Possibly look into making Dependencies an array of tuples of relevent data.
        
        public Func<Container<TData>, Outcome> Processor { get; set; }

        public Outcome Process(Container<TData> container)
        {
            bool identify = Validate(container);

            foreach (IDependency dependency in Dependencies)
            {
                if (dependency.Process(container) is Outcome<Outcome> outcome && !outcome.Positive)
                {
                    Debug.WriteLine($"One of the dependencies of the analyzer \"{Key}\" failed with the following description: {outcome.Message}.");
                    return outcome.Data.Identify(Key, identify && Metadata.HasFlag(Characteristics.Generic));
                }
            }

            return Processor.Invoke(container).Identify(Key, identify && Metadata.HasFlag(Characteristics.Generic));

            static bool Validate(Container<TData> target) => (target.Head, target.Head = false).Head;
        }

        public static implicit operator Dependency(Analyzer<TData> analyzer) => new Dependency<TData> { Analyzer = analyzer };

        public Dependency Depend(TData data) => new Dependency<TData> { Analyzer = this, Data = data };

        public Package<TData> Package(TData data, string key = default) => new Package<TData> { Analyzers = new[] { this }, Data = data, Key = key };
    }

    public class Package<TData> : IPackage
    {
        // TODO: Consider defining Analyzers as an IDependency array.
        // TODO: Look into consolidating container types.

        public string Key { get; set; }

        public TData Data { get; set; }

        public Analyzer<TData>[] Analyzers { get; set; }

        public Outcome[] Evaluation => Analyze(this);

        public static Outcome[] Analyze(Package<TData> package)
        {
            Container<TData> container = new Container<TData> { Data = package.Data };

            return package.Analyzers.Select(analyzer => analyzer.Process(container)).ToArray();
        }
    }

    public static class Packager
    {
        public static Package<TData> Package<TData>(this TData data, params Analyzer<TData>[] analyzers) => new Package<TData> { Analyzers = analyzers, Data = data }; 

        public static Package<TData> Package<TData>(this TData data, string key, params Analyzer<TData>[] analyzers) => new Package<TData> { Analyzers = analyzers, Data = data, Key = key }; 
    }

    public class Analysis : Dictionary<string, Outcome>
    {
        public bool Validated => Values.All(value => value.Positive);

        public Analysis Failures => Build(this.Where(entry => !entry.Value.Positive));

        public Outcome Attach(string key, Outcome value) => base[key] = value;

        public static implicit operator bool(Analysis target) => target?.Validated ?? false;

        public static Analysis Create(params IPackage[] packages) => Validator.Validate(packages);

        internal static Analysis Build(IEnumerable<KeyValuePair<string, Outcome>> entries) => new Analysis { }.Attach(entries);

        public Analysis Attach(IEnumerable<KeyValuePair<string, Outcome>> entires)
        {
            foreach (KeyValuePair<string, Outcome> entry in entires)
            {
                Add(entry.Key, entry.Value);
            }
            return this;
        }
    }

    public static class Validator
    {
        // TODO: Look into if this class is needed.

        public static Analysis Validate(params IPackage[] packages) => Analysis.Build(packages.SelectMany(package => package.Evaluation.Select(outcome => new KeyValuePair<string, Outcome>(package.Key ?? outcome.Key, outcome))));

        public static bool Validate(ref Analysis target, params IPackage[] packages) => target = Validate(packages);
    }
}
