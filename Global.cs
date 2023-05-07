using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleApp4
{
    public sealed class Global
    {
        private class Nested
        {
            static Nested()
            {
            }

            internal static readonly Global source = new Global();
        }
        private Global() { }

        public static Global Source { get { return Nested.source; } }

        enum ObjectType {
            UNIT,
            GROUND,
            BUILDING,
            ALL
        }



    }
}
