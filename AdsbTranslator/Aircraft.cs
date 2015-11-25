using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdsbTranslator
{
    class Aircraft
    {
        public UInt32 addr;
        public UInt32 seen;
        public int odd_cprlat;
        public int odd_cprlon;
        public int even_cprlat;
        public int even_cprlon;
        public double odd_cprtime;
        public double even_cprtime;
        public double lat;
        public double lon;
        public int altitude;
        public int speed;
        public int track;
        public char[] flight;

        public Aircraft()
        {

            flight = new char[9];
        }
    }


}
