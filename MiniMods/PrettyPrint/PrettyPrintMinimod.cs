using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Minimod.PrettyText;
using Minimod.PrettyTypeSignatures;

namespace Minimod.PrettyPrint
{
    /// <summary>
    /// <h1>Minimod.PrettyPrint, Version 0.8.7, Copyright � Lars Corneliussen 2011</h1>
    /// <para>Creates nice textual representations of any objects. Mostly meant for debug/informational output.</para>
    /// </summary>
    /// <remarks>
    /// Licensed under the Apache License, Version 2.0; you may not use this file except in compliance with the License.
    /// http://www.apache.org/licenses/LICENSE-2.0
    /// </remarks>
    public static class PrettyPrintMinimod
    {
        #region Settings

        public static Settings DefaultSettings;

        static PrettyPrintMinimod()
        {
            var settings = new Settings();

            settings.RegisterFormatterFor<Type>(t => t.GetPrettyName());
            settings.RegisterFormatterFor<MethodBase>(m => m.GetPrettyName());

            settings.RegisterFormatterFor<Guid>(formatter);

            DateTimeFormatter.Register(settings);
            TimeSpanFormatter.Register(settings);
            GenericFormatter.Register(settings);
            FileSystemInfoFormatter.Register(settings);

            settings.OmitNullMembers(true);

            DefaultSettings = settings;
        }

        private static string formatter(Guid guid)
        {
            if (guid == Guid.Empty) return "<Guid.Empty>";
            return guid.ToString("D").ToUpperInvariant();
        }

        public static Settings CreateCustomSettings()
        {
            return new Settings(DefaultSettings);
        }

        public class Settings
        {
            private readonly Settings _inner;

            private Dictionary<Type, Func<object, string>> _customFormatters =
                new Dictionary<Type, Func<object, string>>();

            private Dictionary<Type, string> _customPrependedPropNames =
                new Dictionary<Type, string>();

            /// <summary>
            /// if a property formatter is null, the property will be ignored
            /// </summary>
            private Dictionary<Type, Dictionary<string, Func<object, string>>> _customMemberFormatters =
                new Dictionary<Type, Dictionary<string, Func<object, string>>>();

            private bool? _prefersMultiline;

            public bool? PrefersMultiline
            {
                get { return _prefersMultiline ?? (_inner != null ? _inner.PrefersMultiline : null); }
                private set { _prefersMultiline = value; }
            }

            private bool? _omitsNullMembers;

            public bool? OmitsNullMembers
            {
                get { return _omitsNullMembers ?? (_inner != null ? _inner.OmitsNullMembers : null); }
                private set { _omitsNullMembers = value; }
            }

            public Settings()
            {
            }

            public Settings(Settings inner)
                : this()
            {
                _inner = inner;
            }

            public Settings RegisterToStringFor<T>()
            {
                return RegisterToStringFor(typeof(T));
            }

            public Settings RegisterToStringFor(Type type)
            {
                _customFormatters.Add(type, o => o.ToString());
                return this;
            }

            public Settings RegisterFormatterFor(Type type, Func<object, string> formatter)
            {
                _customFormatters.Add(type, formatter);
                return this;
            }

            public Settings RegisterFormatterFor<T>(Func<T, string> formatter)
            {
                _customFormatters.Add(typeof(T), o => formatter((T)o));
                return this;
            }

            public Settings IgnoreMember<T, Prop>(Expression<Func<T, Prop>> member)
            {
                return IgnoreMember(typeof(T), getMemberName(member));
            }

            public Settings IgnoreMember(Type type, string propertyName)
            {
                return RegisterPropertyFormatterFor(type, propertyName, o => null);
            }

            public Settings RegisterPropertyFormatterFor<T, Prop>(Expression<Func<T, Prop>> member,
                                                                  Func<Prop, string> formatter)
            {
                return RegisterPropertyFormatterFor(typeof(T), getMemberName(member), o => formatter((Prop)o));
            }

            public Settings RegisterPropertyFormatterFor(Type type, string propertyName, Func<object, string> formatter)
            {
                if (type == null) throw new ArgumentNullException("type");
                if (propertyName == null) throw new ArgumentNullException("propertyName");
                if (formatter == null) throw new ArgumentNullException("formatter");

                Dictionary<string, Func<object, string>> propFormatters;
                if (!_customMemberFormatters.TryGetValue(type, out propFormatters))
                {
                    _customMemberFormatters[type] = (propFormatters = new Dictionary<string, Func<object, string>>());
                }

                propFormatters.Add(propertyName, formatter);

                return this;
            }

            public Settings RegisterCustomPrependProperty<T, Prop>(Expression<Func<T, Prop>> member)
            {
                return RegisterCustomPrependProperty(typeof(T), getMemberName(member));
            }

            public Settings RegisterCustomPrependProperty(Type type, string propertyName)
            {
                if (type == null) throw new ArgumentNullException("type");
                if (propertyName == null) throw new ArgumentNullException("propertyName");

                _customPrependedPropNames.Add(type, propertyName);

                return this;
            }

            private static string getMemberName(LambdaExpression expression)
            {
                if (expression == null) throw new ArgumentNullException("expression");

                if (expression.Body is MemberExpression)
                {
                    return ((MemberExpression)expression.Body).Member.Name;
                }

                throw new ArgumentException(
                    "Unsupported expression type: " + expression.Body.GetType(), "expression");
            }

            public Func<object, string> GetCustomFormatter(object anyObject)
            {
                return _customFormatters
                           .Where(t => t.Key.IsAssignableFrom(anyObject.GetType()))
                           .Select(kv => kv.Value)
                           .FirstOrDefault()
                       ?? (_inner != null ? _inner.GetCustomFormatter(anyObject) : null);
            }

            internal bool tryGetCustomMemberFormatter(object anyObject, string property,
                                                      out Func<object, string> formatter)
            {
                formatter = null;

                var propsByType = _customMemberFormatters
                    .Where(byType => byType.Key.IsAssignableFrom(anyObject.GetType()))
                    .Select(byType => byType.Value)
                    .SelectMany(dict => dict)
                    .ToDictionary(kv => kv.Key, kv => kv.Value);


                if (propsByType != null && propsByType.TryGetValue(property, out formatter))
                {
                    return true;
                }

                if (_inner == null)
                {
                    return false;
                }

                if (_inner.tryGetCustomMemberFormatter(anyObject, property, out formatter))
                {
                    return true;
                }

                return false;
            }


            public string GetCustomPrependedPropName(Type actualType)
            {
                return _customPrependedPropNames
                           .Where(t => t.Key.IsAssignableFrom(actualType))
                           .Select(kv => kv.Value)
                           .FirstOrDefault()
                       ?? (_inner != null ? _inner.GetCustomPrependedPropName(actualType) : null);
            }

            public Settings PreferMultiline(bool multiline)
            {
                PrefersMultiline = multiline;
                return this;
            }

            public Settings OmitNullMembers(bool omit)
            {
                OmitsNullMembers = omit;
                return this;
            }
        }

        #endregion

        #region Public Extensions

        public static string PrettyPrint<T>(this T anyObject)
        {
            return anyObject.PrettyPrint(typeof(T), DefaultSettings);
        }

        public static string PrettyPrint<T>(this T anyObject, Settings settings)
        {
            return anyObject.PrettyPrint(typeof(T), settings);
        }

        public static string PrettyPrint<T>(this T anyObject, Func<Settings, Settings> customize)
        {
            return anyObject.PrettyPrint(typeof(T), customize(CreateCustomSettings()));
        }

        public static string PrettyPrint(this object anyObject, Type declaredType)
        {
            return anyObject.PrettyPrint(declaredType, DefaultSettings);
        }

        public static string PrettyPrint(this object anyObject, Type declaredType, Func<Settings, Settings> customize)
        {
            return anyObject.PrettyPrint(declaredType, customize(DefaultSettings));
        }

        public static string PrettyPrint(this object anyObject, Type declaredType, Settings settings)
        {
            if (anyObject == null)
            {
                return "<null" + (declaredType != typeof(object) ? ", " + declaredType.GetPrettyName() : "") + ">";
            }

            var formatter = settings.GetCustomFormatter(anyObject);
            if (formatter != null)
            {
                return formatter(anyObject);
            }

            var actualType = anyObject.GetType();

            if (anyObject is string)
            {
                var s = (string)anyObject;
                return s == String.Empty
                           ? "<String.Empty>"
                           : s;
            }

            if (actualType.IsPrimitive)
            {
                return anyObject.ToString();
            }

            if (anyObject is IEnumerable)
            {
                return enumerable(anyObject as IEnumerable, declaredType, settings);
            }

            return GenericFormatter.Format(actualType, anyObject, settings);
        }

        #endregion

        private static string enumerable(IEnumerable objects, Type declaredType, Settings settings)
        {
            string[] items = objects.Cast<object>().Select(_ => _.PrettyPrint(settings)).ToArray();

            if (settings.PrefersMultiline
                ?? (
                       (items.Length > 1 && items.Any(i => i.Length > 30))
                       || (items.Length > 10)
                       || items.Any(i => i.Contains(Environment.NewLine)))
                )
            {
                return "["
                       + Environment.NewLine
                       + String.Join("," + Environment.NewLine, items.Select(i => i.IndentLinesBy(2)).ToArray())
                       + Environment.NewLine
                       + "]";
            }

            return "[" + String.Join(", ", items) + "]";
        }

        private static bool checkIfAnonymousType(Type type)
        {
            if (type == null)
                throw new ArgumentNullException("type");

            // HACK: The only way to detect anonymous types right now.
            return Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute), false)
                   && type.Name.Contains("AnonymousType")
                   && (type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))
                   && (type.Attributes & TypeAttributes.NotPublic) == TypeAttributes.NotPublic;
        }

        /// <summary>
        /// Tries to figure out a nice format on its own.
        /// </summary>
        public class GenericFormatter
        {
            public static void Register(Settings settings)
            {
                settings.RegisterFormatterFor<MemberDetails>(Format);
            }

            public class MemberDetails
            {
                public string Name { get; set; }

                public Type Type { get; set; }

                public object Value { get; set; }

                public string Pretty { get; set; }
            }

            public static string Format(Type actualType, object anyObject, Settings settings)
            {
                if (actualType == null) throw new ArgumentNullException("actualType");
                if (anyObject == null) throw new ArgumentNullException("anyObject");
                if (settings == null) throw new ArgumentNullException("settings");

                var members =
                    findAndFormatMembers(anyObject, settings, actualType)
                        .Where(m => m.Value != null || (settings.OmitsNullMembers ?? false))
                        .ToArray();

                string result;

                if (mayFormatKeyValuePairs(members, out result))
                    return result;

                return formatPropertyList(actualType, members, settings);
            }

            private static bool mayFormatKeyValuePairs(MemberDetails[] members, out string format)
            {
                var keyProperty = members.Where(_ => _.Name == "Key").FirstOrDefault();
                var valueProperty = members.Where(_ => _.Name == "Value").FirstOrDefault();

                if (keyProperty != null && valueProperty != null && members.Length == 2)
                {
                    {
                        format = keyProperty.Pretty + " => " +
                                 valueProperty.Pretty;
                        return true;
                    }
                }
                format = null;
                return false;
            }

            private static IEnumerable<MemberDetails> findAndFormatMembers(object anyObject, Settings settings,
                                                                           Type actualType)
            {
                var properties =
                    from prop in actualType.GetMembers().OfType<PropertyInfo>()
                    where !prop.GetGetMethod().IsStatic && prop.GetIndexParameters().Length == 0
                    select
                        new
                            {
                                name = prop.Name,
                                type = prop.PropertyType,
                                value = prop.GetValue(anyObject, new object[0])
                            };

                var fields =
                    from prop in actualType.GetMembers().OfType<FieldInfo>()
                    where !prop.IsStatic
                    select new { name = prop.Name, type = prop.FieldType, value = prop.GetValue(anyObject) };

                foreach (var m in fields.Union(properties))
                {
                    Func<object, string> propFormatter;
                    bool hasCustomFormatter = settings.tryGetCustomMemberFormatter(anyObject, m.name, out propFormatter);

                    string pretty = hasCustomFormatter
                                        ? propFormatter(m.value)
                                        : m.value.PrettyPrint(m.type, settings);

                    if (pretty != null)
                    {
                        // if formatter returned null, the member should be ignored
                        yield return new MemberDetails { Name = m.name, Type = m.type, Value = m.value, Pretty = pretty };
                    }
                }
            }

            private static string formatPropertyList(Type actualType, MemberDetails[] members, Settings settings)
            {
                var prependedPropName = settings.GetCustomPrependedPropName(actualType) ?? "Name";
                var prependedProp = members.Where(_ => _.Name == prependedPropName).FirstOrDefault();
                var contentProps = members.Where(_ => _.Name != prependedPropName).ToArray();

                List<string> parts = new List<string>();
                if (prependedProp != null && prependedProp.Value != null)
                {
                    parts.Add(prependedProp.Pretty);
                }

                string typeName = checkIfAnonymousType(actualType)
                                  && contentProps.Length > 0
                                      ? null
                                      : actualType.GetPrettyName();

                if (typeName != null)
                {
                    parts.Add("<" + actualType.GetPrettyName() + ">");
                }

                if (contentProps.Length != 0)
                {
                    var printedProps = contentProps
                        .Select(prop => prop.PrettyPrint());


                    if (settings.PrefersMultiline
                        ?? (printedProps.Any(s => s.Length > 30
                            || s.Contains(Environment.NewLine))))
                    {
                        StringBuilder contentBuilder = new StringBuilder();
                        contentBuilder.AppendLine("{");
                        contentBuilder.AppendLine(printedProps.JoinLines());
                        contentBuilder.Append("}");
                        parts.Add(contentBuilder.ToString());
                    }
                    else
                    {
                        parts.Add("{ " + String.Join(", ", printedProps.Select(s => s.Trim()).ToArray()) + " }");
                    }
                }

                return String.Join(" ", parts.Select(s => s.Trim()).Where(s => !String.IsNullOrEmpty(s)).ToArray());
            }

            public static string Format(MemberDetails prop)
            {
                string value = prop.Pretty;

                if (prop.Type == typeof(string)
                    && value.Contains(Environment.NewLine))
                {
                    value = Environment.NewLine + value.IndentLinesBy(2);
                }

                return (prop.Name + " = " + value).IndentLinesBy(2);
            }
        }

        public class DateTimeFormatter
        {
            public static void Register(Settings settings)
            {
                settings.RegisterFormatterFor<DateTime>(Format);
                settings.RegisterFormatterFor<DateTimeOffset>(Format);
            }

            public static string Format(DateTime dateTime)
            {
                if (dateTime == DateTime.MinValue) return "<DateTime.MinValue>";
                if (dateTime == DateTime.MaxValue) return "<DateTime.MaxValue>";

                string kind = "";
                if (dateTime.Kind == DateTimeKind.Utc)
                    kind = " (UTC)";
                else if (dateTime.Kind == DateTimeKind.Local)
                    kind = dateTime.ToString(" (K)");


                if (dateTime.TimeOfDay == TimeSpan.Zero) return dateTime.ToString("yyyy-MM-dd") + kind;
                if (dateTime.Second + dateTime.Millisecond == 0) return dateTime.ToString("yyyy-MM-dd HH:mm") + kind;
                if (dateTime.Millisecond == 0) return dateTime.ToString("yyyy-MM-dd HH:mm:ss") + kind;

                return dateTime.ToString("yyyy-MM-dd HH:mm:ss.fff") + kind;
            }

            public static string Format(DateTimeOffset dateTime)
            {
                return "<DateTimeOffset> { "
                       + Format(dateTime.LocalDateTime) + ", "
                       + Format(dateTime.UtcDateTime)
                       + " }";
            }
        }

        public class TimeSpanFormatter
        {
            public static void Register(Settings settings)
            {
                settings.RegisterFormatterFor<TimeSpan>(Format);
            }

            public static string Format(TimeSpan timeSpan)
            {
                if (timeSpan == TimeSpan.Zero)
                {
                    return "<TimeSpan.Zero>";
                }

                if (timeSpan == TimeSpan.MinValue)
                {
                    return "<TimeSpan.MinValue>";
                }

                if (timeSpan == TimeSpan.MaxValue)
                {
                    return "<TimeSpan.MaxValue>";
                }

                if (timeSpan < TimeSpan.FromSeconds(1))
                {
                    return milliseconds(timeSpan);
                }
                if (timeSpan < TimeSpan.FromMinutes(1))
                {
                    return seconds(timeSpan);
                }
                if (timeSpan < TimeSpan.FromHours(1))
                {
                    return minutes(timeSpan);
                }
                if (timeSpan < TimeSpan.FromHours(24))
                {
                    return hours(timeSpan);
                }

                return days(timeSpan);
            }

            private static string milliseconds(TimeSpan timeSpan)
            {
                if (timeSpan.TotalMilliseconds % 1 == 0)
                {
                    return (int)timeSpan.TotalMilliseconds + " ms";
                }

                return timeSpan.TotalMilliseconds.ToString() + " ms";
            }

            private static string seconds(TimeSpan timeSpan)
            {
                if (timeSpan.TotalSeconds % 1 == 0)
                {
                    return (int)timeSpan.TotalSeconds + " s";
                }

                return string.Format("{0}.{1:D3} s", timeSpan.Seconds, timeSpan.Milliseconds);
            }

            private static string minutes(TimeSpan timeSpan)
            {
                if (timeSpan.TotalMinutes % 1 == 0)
                {
                    return (int)timeSpan.TotalMinutes + " min";
                }

                return string.Format("{0}:{1:D2}" + (timeSpan.Milliseconds == 0 ? "" : ".{2:D3}") + " min",
                                     timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds);
            }

            private static string hours(TimeSpan timeSpan)
            {
                if (timeSpan.TotalHours % 1 == 0)
                {
                    return (int)timeSpan.TotalHours + " h";
                }

                return string.Format("{0}:{1:D2}" + ((timeSpan.TotalMinutes % 1 == 0)
                                                         ? ""
                                                         : ":{2:D2}" + (timeSpan.TotalSeconds % 1 == 0
                                                                            ? ""
                                                                            : ".{3:D3}")) + " h", timeSpan.Hours,
                                     timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds);
            }

            private static string days(TimeSpan timeSpan)
            {
                if (timeSpan.TotalDays % 1 == 0)
                {
                    return (int)timeSpan.TotalDays + " d";
                }

                return string.Format("{0}.{1:D2}" +
                                     ((timeSpan.TotalHours % 1 == 0)
                                          ? ""
                                          : ":{2:D2}" + ((timeSpan.TotalMinutes % 1 == 0)
                                                             ? ""
                                                             : ":{3:D2}" + (timeSpan.TotalSeconds % 1 == 0
                                                                                ? ""
                                                                                : ".{4:D3}"))) + " d", timeSpan.Days,
                                     timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds, timeSpan.Milliseconds);
            }
        }

        /// <summary>
        /// Tries to figure out a nice format on its own.
        /// </summary>
        public class FileSystemInfoFormatter
        {
            public static void Register(Settings settings)
            {
                //settings.RegisterPropertyFormatterFor((DirectoryInfo fs) => fs.Parent, p => p == null ? null : p.FullName);
                settings.RegisterCustomPrependProperty((DirectoryInfo fs) => fs.FullName);
                settings.IgnoreMember((DirectoryInfo fs) => fs.Name);
                settings.IgnoreMember((DirectoryInfo fs) => fs.Parent);
                settings.IgnoreMember((DirectoryInfo fs) => fs.Root);

                settings.RegisterPropertyFormatterFor((FileInfo fs) => fs.Directory, dir => dir.FullName);
                settings.IgnoreMember((FileInfo fs) => fs.DirectoryName);
                settings.IgnoreMember((FileInfo fs) => fs.IsReadOnly);
                settings.IgnoreMember((FileInfo fs) => fs.FullName);


                settings.IgnoreMember((FileSystemInfo fs) => fs.Extension);
                settings.IgnoreMember((FileSystemInfo fs) => fs.CreationTimeUtc);
                settings.IgnoreMember((FileSystemInfo fs) => fs.LastAccessTimeUtc);
                settings.IgnoreMember((FileSystemInfo fs) => fs.LastWriteTimeUtc);
                settings.IgnoreMember((FileSystemInfo fs) => fs.Attributes);
            }
        }
    }
}