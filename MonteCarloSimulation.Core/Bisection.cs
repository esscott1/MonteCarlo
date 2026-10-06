namespace MonteCarloSimulation.Core
{
    // Bisection over a monotone predicate, for the few amounts whose tax has no closed-form inverse (where
    // ordinary income pushes already-realized gains into higher LTCG brackets). Stops once the interval is
    // within a millionth of a dollar, or at floating-point resolution.
    //
    // The engine passes its predicates as structs (IPredicate), so a solve allocates nothing; a capturing lambda
    // would allocate its closure on every call of the method that declares it, even when the solve doesn't run.
    internal static class Bisection
    {
        private const int Iterations = 100;
        private const double Tolerance = 1e-6;

        public interface IPredicate
        {
            bool Holds(double x);
        }

        private readonly struct FuncPredicate(Func<double, bool> ok) : IPredicate
        {
            public bool Holds(double x) => ok(x);
        }

        private static bool Converged(double lo, double hi) => hi - lo <= Tolerance;

        // Smallest x in [lo, hi] for which `ok` holds, given ok is false-then-true across the interval
        // and ok(hi) is true.
        public static double Smallest(double lo, double hi, Func<double, bool> ok) => Smallest(lo, hi, new FuncPredicate(ok));

        public static double Smallest<TPredicate>(double lo, double hi, TPredicate ok) where TPredicate : struct, IPredicate
        {
            for (int i = 0; i < Iterations && !Converged(lo, hi); i++)
            {
                double mid = lo + (hi - lo) / 2;
                if (mid <= lo || mid >= hi) break;
                if (ok.Holds(mid)) hi = mid; else lo = mid;
            }
            return hi;
        }

        // Largest x in [lo, hi] for which `ok` holds, given ok is true-then-false across the interval
        // and ok(lo) is true.
        public static double Largest(double lo, double hi, Func<double, bool> ok) => Largest(lo, hi, new FuncPredicate(ok));

        public static double Largest<TPredicate>(double lo, double hi, TPredicate ok) where TPredicate : struct, IPredicate
        {
            for (int i = 0; i < Iterations && !Converged(lo, hi); i++)
            {
                double mid = lo + (hi - lo) / 2;
                if (mid <= lo || mid >= hi) break;
                if (ok.Holds(mid)) lo = mid; else hi = mid;
            }
            return lo;
        }
    }
}
