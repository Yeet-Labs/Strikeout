using System;
using System.Collections.Generic;
using System.Text;

namespace Strikeout
{
    public static class Utilities
    {
        public static int SafeGetHashCode<TTarget>(this TTarget target) where TTarget : class => target?.GetHashCode() ?? 0;

        public static int SafeGetHashCode(this string target, StringComparison comparison) => target?.GetHashCode(comparison) ?? 0;
    }
}
