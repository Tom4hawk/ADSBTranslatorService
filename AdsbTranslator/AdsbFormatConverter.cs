using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AdsbTranslator
{
    class AdsbFormatConverter
    {
        /* Useful consts */
        const int _longMessageBytesSize = 14;//bytes (112 bits)
        const int _shortMessageBytesSize = 7;//bytes (56 bits)
        const int _longMessageBitsSize = _longMessageBytesSize * 8;
        const int _shortMessageBitsSize = _shortMessageBytesSize * 8;
        const int _unitFeet = 0;
        const int _unitMeter = 1;

        private readonly UInt32[] modes_checksum_table ={
            0x3935ea, 0x1c9af5, 0xf1b77e, 0x78dbbf, 0xc397db, 0x9e31e9, 0xb0e2f0, 0x587178,
            0x2c38bc, 0x161c5e, 0x0b0e2f, 0xfa7d13, 0x82c48d, 0xbe9842, 0x5f4c21, 0xd05c14,
            0x682e0a, 0x341705, 0xe5f186, 0x72f8c3, 0xc68665, 0x9cb936, 0x4e5c9b, 0xd8d449,
            0x939020, 0x49c810, 0x24e408, 0x127204, 0x093902, 0x049c81, 0xfdb444, 0x7eda22,
            0x3f6d11, 0xe04c8c, 0x702646, 0x381323, 0xe3f395, 0x8e03ce, 0x4701e7, 0xdc7af7,
            0x91c77f, 0xb719bb, 0xa476d9, 0xadc168, 0x56e0b4, 0x2b705a, 0x15b82d, 0xf52612,
            0x7a9309, 0xc2b380, 0x6159c0, 0x30ace0, 0x185670, 0x0c2b38, 0x06159c, 0x030ace,
            0x018567, 0xff38b7, 0x80665f, 0xbfc92b, 0xa01e91, 0xaff54c, 0x57faa6, 0x2bfd53,
            0xea04ad, 0x8af852, 0x457c29, 0xdd4410, 0x6ea208, 0x375104, 0x1ba882, 0x0dd441,
            0xf91024, 0x7c8812, 0x3e4409, 0xe0d800, 0x706c00, 0x383600, 0x1c1b00, 0x0e0d80,
            0x0706c0, 0x038360, 0x01c1b0, 0x00e0d8, 0x00706c, 0x003836, 0x001c1b, 0xfff409,
            0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
            0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000,
            0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000, 0x000000
        };


        /* Settings */
        private bool fixSingleBitError;
        private int aircraftTTL;

        private Hashtable icaolist;
        private List<Aircraft> aircraftList;

        private bool createID;//we received  flight number for the first time
        private DecodedMessage currentMessage;
        String messageSBS;

        public AdsbFormatConverter(bool fix1Error = false, int aircraft_TTL = 120)
        {
            fixSingleBitError = fix1Error;
            aircraftTTL = aircraft_TTL;
            icaolist = new Hashtable();
            aircraftList = new List<Aircraft>();
        }

        /* This function prepare hexadecimal string with mode S message to conversion */
        public String convertRaw(string rawMessage)
        {
            messageSBS = string.Empty;
            clearICAOAndAircraftList(); // ICAO and AIRCRAFT lists cleaning - both depend on aircraftTTL
            
            rawMessage = rawMessage.Trim();//remove all whitespaces

            if (rawMessage.Length > ((_longMessageBytesSize * 2) + 2)|| rawMessage.Length < ((_shortMessageBytesSize * 2) + 2) || rawMessage[0] != '*' || rawMessage[rawMessage.Length - 1] != ';')
            {
                return messageSBS; //sorry, wrong format - nothing to do here!
            }

            rawMessage = rawMessage.Remove(0, 1).Remove(rawMessage.Length - 2, 1);//removes "*" and ";" from string

            createID = false;

            byte[] msg = new byte[_longMessageBytesSize];
            
            for (int i = 0; i < rawMessage.Length; i += 2)//convert string with hex values to byte array
            {
                try
                {
                    int high = Convert.ToInt32(rawMessage[i].ToString(), 16);
                    int low = Convert.ToInt32(rawMessage[i + 1].ToString(), 16);
                    msg[i / 2] = (byte)((high << 4) | low);
                }
                catch (Exception)
                {
                    return messageSBS; //sorry, there was something wrong with hex values in this message - nothing to do here!
                }   
            }

            decodeModesMessage(msg);

            return messageSBS;
        }

        private void clearICAOAndAircraftList()
        {
            int time = (int)((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds - aircraftTTL);

            ArrayList arr = new ArrayList();

            foreach (DictionaryEntry message in icaolist)
            {
                if ((int)message.Value < time)
                {
                    arr.Add(message.Key);
                }

            }

            for (int i = 0; i < arr.Count;++i)
            {
                icaolist.Remove(arr[i]);
            }
            for (int i = aircraftList.Count-1; i >= 0; i--)
            {
                if (aircraftList[i].lastSeen < time)
                {
                    if (!String.IsNullOrEmpty(messageSBS))
                        messageSBS += Environment.NewLine;

                    messageSBS += string.Format("STA,,,,{0:X6},,,,,", aircraftList[i].addr);
                    aircraftList.RemoveAt(i);
                }
            }
        }

        private void decodeModesMessage(byte[] msg)
        {
            currentMessage = new DecodedMessage();

            Array.Copy(msg, currentMessage.message, _longMessageBytesSize);

            /* Diffrent messages have diffrent information so we have to decode msg type and size first to determine what we can get from it */
            currentMessage.downlinkFormat = msg[0] >> 3;
            modesMessageLenByType();

            currentMessage.messageCrc = ((UInt32)msg[(currentMessage.msgSize / 8) - 3] << 16) | ((UInt32)msg[(currentMessage.msgSize / 8) - 2] << 8) | (UInt32)msg[(currentMessage.msgSize / 8) - 1];//trzy ostatnie bity to CRC
            UInt32 crc2 = computeChecksum();

            /* Fixing errors in DF11, DF17 if desired */
            currentMessage.correctedBit = -1;//nothing corrected yet
            currentMessage.validCrc = (crc2 == currentMessage.messageCrc);

            if (!currentMessage.validCrc && fixSingleBitError)
            {
                if (currentMessage.downlinkFormat == 11 || currentMessage.downlinkFormat == 17){
                        fixSingleBitErrors();
                }
            }

            currentMessage.responderCapabilities = msg[0] & 7;

            currentMessage.icaoAddrPartOne = msg[1];
            currentMessage.icaoAddrPartTwo = msg[2];
            currentMessage.icaoAddrPartThree = msg[3];

            /* DF 17 */
            currentMessage.esMessageType = msg[4] >> 3;
            currentMessage.esMessageSubType = msg[4] & 7;

            /* DF4, 5, 20, 21 */
            currentMessage.flightStatus = msg[0] & 7;

            calculateSquawk();


             /* For DF 11 & 17 we add icao address to our list of known icao list (if crc is valid..)
              * This way we can validate checksums for other downlink formats where checksum is xored with ICAO
              * but message doesn't contain ICAO addr (we try XOR with every known ICAO on our list)
              */
            if (currentMessage.downlinkFormat == 11 || currentMessage.downlinkFormat == 17)
            {

                if (currentMessage.validCrc && currentMessage.correctedBit == -1)
                {
                    UInt32 addr = (UInt32)((currentMessage.icaoAddrPartOne << 16) | (currentMessage.icaoAddrPartTwo << 8) | currentMessage.icaoAddrPartThree);

                    icaolist[addr] = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                }

            }
            else
            {
                if (bruteForceAP())
                    currentMessage.validCrc = true;
                else
                    currentMessage.validCrc = false;
            }

            /* Decode altitude */
            if (currentMessage.downlinkFormat == 0 || currentMessage.downlinkFormat == 4 || currentMessage.downlinkFormat == 16 || currentMessage.downlinkFormat == 20)
            {
                decodeAC13();
            }
                        
            /* Extended squitter (DF17) */
            if (currentMessage.downlinkFormat == 17)
            {
                String availableCharset = "?ABCDEFGHIJKLMNOPQRSTUVWXYZ????? ???????????????0123456789??????";//available chars
   
                if (currentMessage.esMessageType >= 1 && currentMessage.esMessageType <= 4)//flight number
                {
                    currentMessage.flightNumber[0] = availableCharset[msg[5] >> 2];
                    currentMessage.flightNumber[1] = availableCharset[((msg[5] & 3) << 4) | (msg[6] >> 4)];
                    currentMessage.flightNumber[2] = availableCharset[((msg[6] & 15) << 2) | (msg[7] >> 6)];
                    currentMessage.flightNumber[3] = availableCharset[msg[7] & 63];
                    currentMessage.flightNumber[4] = availableCharset[msg[8] >> 2];
                    currentMessage.flightNumber[5] = availableCharset[((msg[8] & 3) << 4) | (msg[9] >> 4)];
                    currentMessage.flightNumber[6] = availableCharset[((msg[9] & 15) << 2) | (msg[10] >> 6)];
                    currentMessage.flightNumber[7] = availableCharset[msg[10] & 63];
                    currentMessage.flightNumber[8] = '\0';
                }
                else if (currentMessage.esMessageType >= 9 && currentMessage.esMessageType <= 18)//position
                {
                    currentMessage.parityFlag = msg[6] & (1 << 2);
                    decodeAC12();

                    currentMessage.rawLatitude = ((msg[6] & 3) << 15) | (msg[7] << 7) | (msg[8] >> 1);
                    currentMessage.rawLongitude = ((msg[8] & 1) << 16) | (msg[9] << 8) | msg[10];
                }
                else if (currentMessage.esMessageType == 19 && currentMessage.esMessageSubType >= 1 && currentMessage.esMessageSubType <= 4)//Velocity
                {
                    if (currentMessage.esMessageSubType == 1 || currentMessage.esMessageSubType == 2)
                    {
                        currentMessage.ewDirection = (msg[5] & 4) >> 2;
                        currentMessage.nsDirection = (msg[7] & 0x80) >> 7;

                        currentMessage.ewVelocity = ((msg[5] & 3) << 8) | msg[6];
                        currentMessage.nsVelocity = ((msg[7] & 0x7f) << 3) | ((msg[8] & 0xe0) >> 5);
                        
                        currentMessage.verticalRateSign = (msg[8] & 0x8) >> 3;

                        currentMessage.verticalRate = ((msg[8] & 7) << 6) | ((msg[9] & 0xfc) >> 2);

                        currentMessage.velocity = (int)Math.Round(Math.Sqrt(currentMessage.nsVelocity * currentMessage.nsVelocity + currentMessage.ewVelocity * currentMessage.ewVelocity));
                        
                        if (currentMessage.velocity != 0)
                        {
                            int ewv = currentMessage.ewVelocity;
                            int nsv = currentMessage.nsVelocity;
                            double heading;
                            
                            if (currentMessage.ewDirection != 0)
                                ewv *= -1;
                            if (currentMessage.nsDirection != 0)
                                nsv *= -1;

                            heading = Math.Atan2(ewv, nsv);
                            
                            currentMessage.track = (int)(heading * 360 / (Math.PI * 2));//keep data in degrees
                           
                            if (currentMessage.track < 0)
                                currentMessage.track += 360;
                        }
                        else
                        {
                            currentMessage.track = 0;
                        }
                    }
                    else if (currentMessage.esMessageSubType == 3 || currentMessage.esMessageSubType == 4)
                    {
                        currentMessage.track = (int)((360.0 / 128) * (((msg[5] & 3) << 5) | (msg[6] >> 3)));
                    }
                }
            }

            if (currentMessage.validCrc)
                convertToSBS(getCurrentAircraft());
        }

        /* Method creates or modifies existing aircraft. It also decodes informations which are using data from multiple messages */
        private Aircraft getCurrentAircraft()
        {
            UInt32 icao;
            Aircraft aircraft;
            Boolean newAircraft = false;

            
            icao = (UInt32)((currentMessage.icaoAddrPartOne << 16) | (currentMessage.icaoAddrPartTwo << 8) | currentMessage.icaoAddrPartThree);
            aircraft = findAircraft(icao);

            if (aircraft == null)//new aircraft
            {
                aircraft = createAircraft(icao);
                aircraftList.Add(aircraft);
                newAircraft = true;
            }

            aircraft.lastSeen = (uint)((DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);

            if (currentMessage.downlinkFormat == 17)
            {
                if (currentMessage.esMessageType >= 1 && currentMessage.esMessageType <= 4)
                {

                    string messageFlight = new string(currentMessage.flightNumber);
                    string aircraftFlight = new string(aircraft.flight);

                    int c = string.Compare(messageFlight, aircraftFlight);
                    if (c != 0)//it's brand new plane, we have to write ID sbs msg!
                    {
                        createID = true;
                        Array.Copy(currentMessage.flightNumber, aircraft.flight, 9);
                    }
                }
                else if (currentMessage.esMessageType >= 9 && currentMessage.esMessageType <= 18)
                {
                    UInt32 currentTime = (UInt32)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                    if (currentMessage.parityFlag != 0)
                    {
                        aircraft.oddFrameCprLatitude = currentMessage.rawLatitude;
                        aircraft.oddFrameCprLongitude = currentMessage.rawLongitude;
                        aircraft.oddFrameCprTime = currentTime;
                    }
                    else
                    {
                        aircraft.evenFrameCprLatitude = currentMessage.rawLatitude;
                        aircraft.evenFrameCprLongitude = currentMessage.rawLongitude;
                        aircraft.evenFrameCprTime = currentTime;
                    }
                    /* We decode full information only when time between frames is =< 10 seconds */
                    if (Math.Abs(aircraft.evenFrameCprTime - aircraft.oddFrameCprTime) <= 10)
                    {
                        decodeCPR(aircraft);
                    }
                }
            }

            if(newAircraft){
                if (!String.IsNullOrEmpty(messageSBS))
                    messageSBS += Environment.NewLine;

                messageSBS += string.Format("AIR,,,,{0:X2}{1:X2}{2:X2},,,,,", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree);
            }
            return aircraft;

        }

        private int cprModFunction(int a, int b) {
            int res = a % b;
            if (res < 0) res += b;
            return res;
        }

        private int cprNLFunction(double lat)
        {
            if (lat < 0) lat = -lat; /* Table is simmetric about the equator. */
            if (lat < 10.47047130) return 59;
            if (lat < 14.82817437) return 58;
            if (lat < 18.18626357) return 57;
            if (lat < 21.02939493) return 56;
            if (lat < 23.54504487) return 55;
            if (lat < 25.82924707) return 54;
            if (lat < 27.93898710) return 53;
            if (lat < 29.91135686) return 52;
            if (lat < 31.77209708) return 51;
            if (lat < 33.53993436) return 50;
            if (lat < 35.22899598) return 49;
            if (lat < 36.85025108) return 48;
            if (lat < 38.41241892) return 47;
            if (lat < 39.92256684) return 46;
            if (lat < 41.38651832) return 45;
            if (lat < 42.80914012) return 44;
            if (lat < 44.19454951) return 43;
            if (lat < 45.54626723) return 42;
            if (lat < 46.86733252) return 41;
            if (lat < 48.16039128) return 40;
            if (lat < 49.42776439) return 39;
            if (lat < 50.67150166) return 38;
            if (lat < 51.89342469) return 37;
            if (lat < 53.09516153) return 36;
            if (lat < 54.27817472) return 35;
            if (lat < 55.44378444) return 34;
            if (lat < 56.59318756) return 33;
            if (lat < 57.72747354) return 32;
            if (lat < 58.84763776) return 31;
            if (lat < 59.95459277) return 30;
            if (lat < 61.04917774) return 29;
            if (lat < 62.13216659) return 28;
            if (lat < 63.20427479) return 27;
            if (lat < 64.26616523) return 26;
            if (lat < 65.31845310) return 25;
            if (lat < 66.36171008) return 24;
            if (lat < 67.39646774) return 23;
            if (lat < 68.42322022) return 22;
            if (lat < 69.44242631) return 21;
            if (lat < 70.45451075) return 20;
            if (lat < 71.45986473) return 19;
            if (lat < 72.45884545) return 18;
            if (lat < 73.45177442) return 17;
            if (lat < 74.43893416) return 16;
            if (lat < 75.42056257) return 15;
            if (lat < 76.39684391) return 14;
            if (lat < 77.36789461) return 13;
            if (lat < 78.33374083) return 12;
            if (lat < 79.29428225) return 11;
            if (lat < 80.24923213) return 10;
            if (lat < 81.19801349) return 9;
            if (lat < 82.13956981) return 8;
            if (lat < 83.07199445) return 7;
            if (lat < 83.99173563) return 6;
            if (lat < 84.89166191) return 5;
            if (lat < 85.75541621) return 4;
            if (lat < 86.53536998) return 3;
            if (lat < 87.00000000) return 2;
            else return 1;
        }

        private double cprDlonFunction(double lat, int isodd)
        {
            return 360.0 / cprNFunction(lat, isodd);
        }

        private int cprNFunction(double lat, int isodd)
        {
            int nl = cprNLFunction(lat) - isodd;
            if (nl < 1) nl = 1;
            return nl;
        }

        /* http://www.lll.lu/~edward/edward/adsb/DecodingADSBposition.html */
        private void decodeCPR(Aircraft a)
        {
            const double AirDlat0 = 360.0 / 60;
            const double AirDlat1 = 360.0 / 59;
            double lat0 = a.evenFrameCprLatitude;
            double lat1 = a.oddFrameCprLatitude;
            double lon0 = a.evenFrameCprLongitude;
            double lon1 = a.oddFrameCprLongitude;

            double tempLatitude;
            double tempLongitude;

            int j = (int)Math.Floor(((59 * lat0 - 60 * lat1) / 131072) + 0.5);
            double rlat0 = AirDlat0 * (cprModFunction(j, 60) + lat0 / 131072);
            double rlat1 = AirDlat1 * (cprModFunction(j, 59) + lat1 / 131072);
    
            if (rlat0 >= 270) rlat0 -= 360;
            if (rlat1 >= 270) rlat1 -= 360;

            if (cprNLFunction(rlat0) != cprNLFunction(rlat1)) return;

            if (a.evenFrameCprTime > a.oddFrameCprTime)
            {
                int ni = cprNFunction(rlat0, 0);
                int m = (int)Math.Floor((((lon0 * (cprNLFunction(rlat0) - 1)) -
                (lon1 * cprNLFunction(rlat0))) / 131072) + 0.5);
                tempLongitude = cprDlonFunction(rlat0, 0) * (cprModFunction(m, ni) + lon0 / 131072);
                tempLatitude = rlat0;
            }
            else
            {
                int ni = cprNFunction(rlat1, 1);
                int m = (int)Math.Floor((((lon0 * (cprNLFunction(rlat1) - 1)) -
                (lon1 * cprNLFunction(rlat1))) / 131072.0) + 0.5);
                tempLongitude = cprDlonFunction(rlat1, 1) * (cprModFunction(m, ni) + lon1 / 131072);
                tempLatitude = rlat1;
            }
            if (tempLongitude > 180) tempLongitude -= 360;

            currentMessage.longitude = tempLongitude;
            currentMessage.latitude = tempLatitude;
        }

        private Aircraft createAircraft(uint addr)
        {
            Aircraft aircraft = new Aircraft();
            aircraft.addr = addr;

            return aircraft;
        }

        private Aircraft findAircraft(uint addr)
        {
            foreach (Aircraft aircraft in aircraftList)
            {
                if(aircraft.addr == addr){
                    return aircraft;
                }
            }

            return null;
        }

        private void modesMessageLenByType()
        {
            if (currentMessage.downlinkFormat == 16 || currentMessage.downlinkFormat == 17 || currentMessage.downlinkFormat == 19 || currentMessage.downlinkFormat == 20 || currentMessage.downlinkFormat == 21)
                currentMessage.msgSize = _longMessageBitsSize;//112;
            else
                currentMessage.msgSize = _shortMessageBitsSize;//56;
        }

        private UInt32 computeChecksum()//just a plug
        {
            return computeChecksum(currentMessage.message);
        }

        /* Return 24bit checksum */
        private UInt32 computeChecksum(byte[] array)
        {
            UInt32 crc = 0;
            int offset = (currentMessage.msgSize == 112) ? 0 : (112 - 56);
            int j;

            for (j = 0; j < currentMessage.msgSize; j++)
            {
                int specByte = j / 8;
                int bit = j % 8;
                int bitmask = 1 << (7 - bit);
                /* If bit is set, xor with corresponding table entry. */
                if ((array[specByte] & bitmask) != 0)
                    crc ^= modes_checksum_table[j + offset];
            }

            return crc;
        }

        /* Changes every bit one by one and check checksum again 
         * On succes changes original message and sets the posiotion of modified bit
         * Otherwise returns -1
         */
        private void fixSingleBitErrors() {
            int j;
            byte[] tempMsg = new byte[_longMessageBytesSize];

            for (j = 0; j < currentMessage.msgSize; j++) {
                int currentByte = j/8;
                byte bitmask = (byte)(1 << (7-(j%8)));
                UInt32 crc1, crc2;

                Array.Copy(currentMessage.message, tempMsg, _longMessageBytesSize);

                tempMsg[currentByte] ^= bitmask; /* Flip j-th bit. */
                crc1 = ((UInt32)tempMsg[(currentMessage.msgSize / 8) - 3] << 16) | ((UInt32)tempMsg[(currentMessage.msgSize / 8) - 2] << 8) | (UInt32)tempMsg[(currentMessage.msgSize / 8) - 1];
                crc2 = computeChecksum(tempMsg);
            
                if (crc1 == crc2) {
                    Array.Copy(tempMsg, currentMessage.message, _longMessageBytesSize);
                    currentMessage.correctedBit = j;
                    currentMessage.validCrc = true;
                    currentMessage.messageCrc = crc1;

                    return;
                }
            }

            currentMessage.correctedBit = -1; //nothing corrected
        }

        private void calculateSquawk()
        {
            int a = ((currentMessage.message[3] & 0x80) >> 5) | ((currentMessage.message[2] & 0x02) >> 0) | ((currentMessage.message[2] & 0x08) >> 3);
            int b = ((currentMessage.message[3] & 0x02) << 1) | ((currentMessage.message[3] & 0x08) >> 2) | ((currentMessage.message[3] & 0x20) >> 5);
            int c = ((currentMessage.message[2] & 0x01) << 2) | ((currentMessage.message[2] & 0x04) >> 1) | ((currentMessage.message[2] & 0x10) >> 4);
            int d = ((currentMessage.message[3] & 0x01) << 2) | ((currentMessage.message[3] & 0x04) >> 1) | ((currentMessage.message[3] & 0x10) >> 4);
            currentMessage.squawkIdentity = a * 1000 + b * 100 + c * 10 + d;
        }

        private void decodeAC13()
        {
            int m_bit = currentMessage.message[3] & (1 << 6);
            int q_bit = currentMessage.message[3] & (1 << 4);

            if (m_bit == 0)
            {
                currentMessage.unit = _unitFeet;
                if (q_bit != 0)
                {
                    /* N is the 11 bit integer resulting from the removal of bit Q and M */
                    int n = ((currentMessage.message[2] & 31) << 6) | ((currentMessage.message[3] & 0x80) >> 2) | ((currentMessage.message[3] & 0x20) >> 1) | (currentMessage.message[3] & 15);
                    /* The final altitude is due to the resulting number multiplied by 25, minus 1000. */
                    currentMessage.altitude = n * 25 - 1000;
                }
                else
                {   //Should not happen in europe
                    // N is an 11 bit Gillham coded altitude
                    int n = ModeAToModeC(decodeID13Field(currentMessage.message[3]));
                    
                    if (n < -12) n = 0;

                    currentMessage.altitude = (100 * n);
                 }
            }
            else
            {
                currentMessage.unit = _unitMeter;
                currentMessage.altitude = 0;
                /* nie ma praktycznie możliwości na wysokość w metrach, nie można znaleźć jkihkolwiek informacji ani sampli jak to liczyć */
            }
        }

        /* https://github.com/MalcolmRobb/dump1090/blob/master/mode_ac.c#L319 */
        private int ModeAToModeC(int ModeA)
        {
            UInt32 FiveHundreds = 0;
            UInt32 OneHundreds = 0;

            if (((ModeA & 0xFFFF888B) != 0) // D1 set is illegal. D2 set is > 62700ft which is unlikely
            || ((ModeA & 0x000000F0) == 0)) // C1,,C4 cannot be Zero
            { return -9999; }
            if ((ModeA & 0x0010) != 0) { OneHundreds ^= 0x007; } // C1
            if ((ModeA & 0x0020) != 0) { OneHundreds ^= 0x003; } // C2
            if ((ModeA & 0x0040) != 0) { OneHundreds ^= 0x001; } // C4
            // Remove 7s from OneHundreds (Make 7->5, snd 5->7).
            if ((OneHundreds & 5) == 5) { OneHundreds ^= 2; }
            // Check for invalid codes, only 1 to 5 are valid
            if (OneHundreds > 5)
            { return -9999; }
            //if (ModeA & 0x0001) {FiveHundreds ^= 0x1FF;} // D1 never used for altitude
            if ((ModeA & 0x0002) != 0) { FiveHundreds ^= 0x0FF; } // D2
            if ((ModeA & 0x0004) != 0) { FiveHundreds ^= 0x07F; } // D4
            if ((ModeA & 0x1000) != 0) { FiveHundreds ^= 0x03F; } // A1
            if ((ModeA & 0x2000) != 0) { FiveHundreds ^= 0x01F; } // A2
            if ((ModeA & 0x4000) != 0) { FiveHundreds ^= 0x00F; } // A4
            if ((ModeA & 0x0100) != 0) { FiveHundreds ^= 0x007; } // B1
            if ((ModeA & 0x0200) != 0) { FiveHundreds ^= 0x003; } // B2
            if ((ModeA & 0x0400) != 0) { FiveHundreds ^= 0x001; } // B4
            // Correct order of OneHundreds.
            if ((FiveHundreds & 1) != 0) { OneHundreds = 6 - OneHundreds; }
            return (int)((FiveHundreds * 5) + OneHundreds - 13);
        }

        private int decodeID13Field(int ID13Field)
        {
            int hexGillham = 0;
            if ((ID13Field & 0x1000) != 0) { hexGillham |= 0x0010; } // Bit 12 = C1
            if ((ID13Field & 0x0800) != 0) { hexGillham |= 0x1000; } // Bit 11 = A1
            if ((ID13Field & 0x0400) != 0) { hexGillham |= 0x0020; } // Bit 10 = C2
            if ((ID13Field & 0x0200) != 0) { hexGillham |= 0x2000; } // Bit 9 = A2
            if ((ID13Field & 0x0100) != 0) { hexGillham |= 0x0040; } // Bit 8 = C4
            if ((ID13Field & 0x0080) != 0) { hexGillham |= 0x4000; } // Bit 7 = A4
            //if (ID13Field & 0x0040) {hexGillham |= 0x0800;} // Bit 6 = X or M
            if ((ID13Field & 0x0020) != 0) { hexGillham |= 0x0100; } // Bit 5 = B1
            if ((ID13Field & 0x0010) != 0) { hexGillham |= 0x0001; } // Bit 4 = D1 or Q
            if ((ID13Field & 0x0008) != 0) { hexGillham |= 0x0200; } // Bit 3 = B2
            if ((ID13Field & 0x0004) != 0) { hexGillham |= 0x0002; } // Bit 2 = D2
            if ((ID13Field & 0x0002) != 0) { hexGillham |= 0x0400; } // Bit 1 = B4
            if ((ID13Field & 0x0001) != 0) { hexGillham |= 0x0004; } // Bit 0 = D4
            return (hexGillham);
        }

        /* Decode the 12 bit AC altitude field (in DF 17 and others).*/
        private void decodeAC12()
        {
            int q_bit = currentMessage.message[5] & 1;
            currentMessage.unit = _unitFeet;
            if (q_bit != 0)
            {
                /* N is the 11 bit integer resulting from the removal of bit Q */
                
                int n = ((currentMessage.message[5] >> 1) << 4) | ((currentMessage.message[6] & 0xF0) >> 4);
                /* The final altitude is due to the resulting number multiplied by 25, minus 1000 */
                currentMessage.altitude = n * 25 - 1000;
            }
            else
            { //Not in Europe
                int n = ((currentMessage.message[5] & 0x0FC0) << 1) | (currentMessage.message[5] & 0x003F);
                n = ModeAToModeC(decodeID13Field(n));
                if (n < -12) { n = 0; }
                currentMessage.altitude = (100 * n);
            }
        }

        bool bruteForceAP()
        {
            byte[] temp = new byte[_longMessageBytesSize];
            int msgtype = currentMessage.downlinkFormat;
            int msgbits = currentMessage.msgSize;
            
            /* 0 - Short air surveillance;
             * 4- Surveillance, altitude reply;
             * 5 - Surveillance, identity reply;
             * 16 - Long Air-Air survillance;
             * 20 - Comm-A, altitude request;
             * 21 - Comm-A, identity request;
             * 24 - Comm-C ELM */
            if (msgtype == 0 || msgtype == 4 || msgtype == 5 || msgtype == 16 || msgtype == 20 || msgtype == 21 || msgtype == 24) 
            {
                UInt32 icao;
                UInt32 crc;
                int lastByte = (msgbits / 8) - 1;

                Array.Copy(currentMessage.message, temp, msgbits / 8);

                crc = computeChecksum(temp);
                temp[lastByte] ^= (byte)(crc & 0xff);
                temp[lastByte - 1] ^= (byte)((crc >> 8) & 0xff);
                temp[lastByte - 2] ^= (byte)((crc >> 16) & 0xff);
                /* If the obtained address exists in our cache we consider the message valid. */
                icao = (UInt32)(temp[lastByte] | (temp[lastByte - 1] << 8) | (temp[lastByte - 2] << 16));
                if (icaolist.ContainsKey(icao))
                {
                    currentMessage.icaoAddrPartOne = temp[lastByte - 2];
                    currentMessage.icaoAddrPartTwo = temp[lastByte - 1];
                    currentMessage.icaoAddrPartThree = temp[lastByte];
                    return true;
                }
            }
            return false;
        }

        /* modesSendSBSOutput*/
        internal void convertToSBS(Aircraft a)
        {
            String tempRespond="";
            int emergency = 0, ground = 0, alert = 0, spi = 0;

            if (currentMessage.downlinkFormat == 4 || currentMessage.downlinkFormat == 5 || currentMessage.downlinkFormat == 21)
            {
                /* Node: identity is calculated/kept in base10 but is actually octal (07500 is represented as 7500) */
                if (currentMessage.squawkIdentity == 7500 || currentMessage.squawkIdentity == 7600 || currentMessage.squawkIdentity == 7700)
                    emergency = -1;
                if (currentMessage.flightStatus == 1 || currentMessage.flightStatus == 3)
                    ground = -1;
                if (currentMessage.flightStatus == 2 || currentMessage.flightStatus == 3 || currentMessage.flightStatus == 4)
                    alert = -1;
                if (currentMessage.flightStatus == 4 || currentMessage.flightStatus == 5)
                    spi = -1;
            }

            if (currentMessage.downlinkFormat == 0)
            {
                tempRespond = string.Format("MSG,5,,,{0:X2}{1:X2}{2:X2},,,,,,,{3:D},,,,,,,,,,", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree, currentMessage.altitude);
            }
            else if (currentMessage.downlinkFormat == 4)
            {
                tempRespond = string.Format("MSG,5,,,{0:X2}{1:X2}{2:X2},,,,,,,{3:D},,,,,,,{4:D},{5:D},{6:D},{7:D}", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree, currentMessage.altitude, alert, emergency, spi, ground);
            }
            else if (currentMessage.downlinkFormat == 5)
            {
                tempRespond = string.Format("MSG,6,,,{0:X2}{1:X2}{2:X2},,,,,,,,,,,,,{3:D},{4:D},{5:D},{6:D},{7:D}", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree, currentMessage.squawkIdentity, alert, emergency, spi, ground);
            }
            else if (currentMessage.downlinkFormat == 11)
            {
                tempRespond = string.Format("MSG,8,,,{0:X2}{1:X2}{2:X2},,,,,,,,,,,,,,,,,", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree);
            }
            else if (currentMessage.downlinkFormat == 17 && currentMessage.esMessageType == 4)
            {
                string flightNumber = new string(currentMessage.flightNumber);
                if (createID)
                {
                    tempRespond = string.Format("ID,,,,{0:X2}{1:X2}{2:X2},,,,,,{3},,,,,,,,,,,", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree, flightNumber);
                }
                else
                {
                    tempRespond = string.Format("MSG,1,,,{0:X2}{1:X2}{2:X2},,,,,,{3},,,,,,,,,,,", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree, flightNumber);
                }
                
            }
            else if (currentMessage.downlinkFormat == 17 && currentMessage.esMessageType >= 9 && currentMessage.esMessageType <= 18)
            {
                if (currentMessage.latitude == 0 && currentMessage.longitude == 0)
                    tempRespond = string.Format("MSG,3,,,{0:X2}{1:X2}{2:X2},,,,,,,{3:D},,,,,,,0,0,0,0", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree, currentMessage.altitude);
                else
                {
                    String tempLat = string.Format("{0:F5}", currentMessage.latitude).Replace(",", ".");
                    String tempLon = string.Format("{0:F5}", currentMessage.longitude).Replace(",", ".");
                    tempRespond = string.Format("MSG,3,,,{0:X2}{1:X2}{2:X2},,,,,,,{3:D},,,{4},{5},,,0,0,0,0", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree, currentMessage.altitude, tempLat, tempLon);
                }
            }
            else if (currentMessage.downlinkFormat == 17 && currentMessage.esMessageType == 19 && currentMessage.esMessageSubType == 1)
            {
                int vr = (currentMessage.verticalRateSign == 0 ? 1 : -1) * (currentMessage.verticalRate - 1) * 64;
                tempRespond = string.Format("MSG,4,,,{0:X2}{1:X2}{2:X2},,,,,,,,{3:D},{4:D},,,{5:D},,0,0,0,0", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree, currentMessage.velocity, currentMessage.track, vr);
            }
            else if (currentMessage.downlinkFormat == 21)
            {
                tempRespond = string.Format("MSG,6,,,{0:X2}{1:X2}{2:X2},,,,,,,,,,,,,{3:D},{4:D},{5:D},{6:D},{7:D}", currentMessage.icaoAddrPartOne, currentMessage.icaoAddrPartTwo, currentMessage.icaoAddrPartThree, currentMessage.squawkIdentity, alert, emergency, spi, ground);
            }

            if (!String.IsNullOrEmpty(messageSBS))
                messageSBS += Environment.NewLine;

            messageSBS += tempRespond;

        }
    }
}