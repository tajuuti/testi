using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace Peliasiakas1
{
    class Program
    {
        private static PeliAsiakas asiakasV;
        private const string FILENAME = @"i:\opiskelu\testaus2016\testitulokset.txt";

        static void Main(string[] args)
        {
            asiakasV = new PeliAsiakas();
            //Kuuntelijat tapahtumille
            asiakasV.ToinenPelaaja += PelaajaSaapui;
            asiakasV.LahtiPois += PelaajaLahti;
            asiakasV.PeliAloitus += AloitetaanUusi;
            asiakasV.Chattaa += PelaajaViestitti;
            asiakasV.MoveTo += SiirraTahan;
            //Verkkoyhteys
            List<string> ipt = new List<string>();
            asiakasV.StartConnection(ref ipt);
            asiakasV.StartListen();
            //Testien nimet
            String[] testinimi = { "T1_1a", "T1_1b", "T1_1c" };
            //Testitaulukko;  vastaanottajan testiaskeleet
            Action[] testi = { TilaStart, TilaStart, TilaStart };
            //Käydään halutut testit läpi
            for (int i = 0; i < testi.Length; i++)
            {  
                Console.WriteLine(testinimi[i]);
                //Testin nimi tiedostoon
                System.IO.File.AppendAllText(FILENAME, "\r\n" + testinimi[i]);
                //Lähtötilan asetus
                testi[i]();
                Console.WriteLine("Uusi testi (return=kyllä, q=lopeta)?");
                String merkki = Console.ReadLine();
                //Peliasiakas-komponentin tila testin jälkeen
                //Testitaulukon 3 tapauksia testattaessa lisätään tiedostoon myös Rivi1 ja Sarake1
                System.IO.File.AppendAllText(FILENAME, "\r\nstate=" + asiakasV.State);
                if (merkki.ToLower().Equals("q")) break;
            }
            Console.WriteLine("Kaikki testit on suoritettu. OK? [ret]");
            Console.ReadKey();
            //Suljetaan lopuksi yhteys
            asiakasV.CloseConnection();
        }

        /// <summary>
        /// Vastaanottajan testitoimi
        /// </summary>
        private static void TilaStart()
        {
            asiakasV.State = "start";
        }

        /// <summary>
        /// Saatu tieto toisen pelaajan saapumisesta.
        /// </summary>
        /// <param name="nimi"></param>
        private static void PelaajaSaapui(string nimi)
        {
            Console.WriteLine("Saapui pelaaja: " + nimi);
            String vaste = "\r\ntapahtuma=PelaajaSaapui\r\nnimi=" + nimi;
            System.IO.File.AppendAllText(FILENAME, vaste);
        }

        private static void PelaajaLahti()
        {
            Console.WriteLine("Toinen pelaaja poistui pelistä");
            System.Threading.Thread.Sleep(200);
            String vaste = "\r\ntapahtuma=PelaajaLahti\r\nconnected=" + asiakasV.connected();
            System.IO.File.AppendAllText(FILENAME, vaste);
        }

        private static void AloitetaanUusi(int vuoro, int ruutuja)
        {
            Console.WriteLine("Aloitetaan uusi peli");
            Console.WriteLine("aloittaja=" + vuoro);
            Console.WriteLine("laudan koko=" + ruutuja);
        }

        private static void PelaajaViestitti(string viesti)
        {
            Console.WriteLine("Saapui chat-viesti");
        }

        private static void SiirraTahan(int r1, int s1, int r2, int s2, bool syodaanko)
        {
            Console.WriteLine("Suoritetaan siirto");
            Console.WriteLine("Riviltä " + r1 + " ja sarakkeesta " + s1);
            Console.WriteLine("Riville " + r2 + " ja sarakkeelle " + s2);
            Console.WriteLine("Syödäänkö: " + syodaanko);
        }
    }

    /// <summary>
    /// Peliasiakkaiden välinen verkkoviestintä.
    /// </summary>
    public class PeliAsiakas
    {
        //oletusIP
        //private const string IP = "127.0.0.1";
        //portti, jossa kuunnellaan toista pelaajaa
        private const int PORT = 8888;
        //Koneen oma ip-osoite, käytetään tätä localhostin asemasta
        IPAddress omaIP;
        //varsinainen yhteyssoketti
        private Socket socket;
        //yhteydenottoa kuunteleva soketti
        private Socket listener;
        private NetworkStream ns;
        private StreamReader sr;
        private StreamWriter sw;
        //peliasiakkaan tila
        private String state;
        //viestienn kuuntelijaolio
        private AsiakasKuuntelee askuuntele;
        //thredi viestien kuuntelulle
        private Thread threadKuuntele;
        //siirrettävän nappulan rivi ja sarake
        private int rivi1, sarake1;

        public PeliAsiakas()
        {
            state = "wait";
            rivi1 = -1;
            sarake1 = -1;
        }

        /// <summary>
        /// Tilamuuttujan asettaminen ja palauttaminen testausta varten.
        /// </summary>
        public String State
        {
            set { state = (String)value; }
            get { return state; }
        }

        /// <summary>
        /// Nappulan rivin asettaminen ja palauttaminen testausta varten.
        /// </summary>
        public int Rivi1
        {
            set { rivi1 = (int)value; }
            get { return rivi1; }
        }

        /// <summary>
        /// Nappulan sarakkeen asettaminen ja palauttaminen testausta varten.
        /// </summary>
        public int Sarake1
        {
            set { sarake1 = (int)value; }
            get { return sarake1; }
        }

        public bool connected()
        {
            return socket.Connected;
        }

        //Tapahtumaviestit pääikkunalle ja pelilogiikalle
        //Viestitään toisesta pelaajasta
        public delegate void Pelaaja2(string pnimi);
        public event Pelaaja2 ToinenPelaaja;

        //Viestitään moveto-siirrosta
        public delegate void ToSiirto(int x1, int y1, int x2, int y2, bool s);
        public event ToSiirto MoveTo;

        //Viestitään pelaajan poistumisesta
        public delegate void Postui();
        public event Postui LahtiPois;

        //Viestitään uudesta pelistä
        public delegate void PelUusiPeli(int lkm, int p);
        public event PelUusiPeli PeliAloitus;

        //Viestitään chat-viestistä
        public delegate void Chatviesti(string msg);
        public event Chatviesti Chattaa;


        /// <summary>
        /// Aloitetaan yhteys verkkoon.
        /// </summary>
        /// <returns>onko toista pelaajaa</returns>
        public bool StartConnection(ref List<string> ipt)
        {
            omaIP = PoimiKoneenIP();
            ipt.Add(omaIP.ToString()); //lisätään koneen oma ip-osoite automaattisesti
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //Yritetään etsiä toista pelaajaa valittujen ip-osoitteiden perusteella.
            try
            {
                foreach (string ip in ipt)
                {
                    IPAddress ipAddress = IPAddress.Parse(ip);
                    socket.Connect(ipAddress, PORT);
                    if (socket.Connected) break;
                }
                if (socket.Connected)
                {
                    ns = new NetworkStream(socket);
                    sw = new StreamWriter(ns, System.Text.Encoding.ASCII);
                    state = "start";
                    LuoThread();
                }
                return true;
            }
            //Jos kukaan ei vastaa aletaan kuunnella toista pelaajaa
            catch (Exception)
            {
                Console.WriteLine("Ei vielä toista pelaajaa");
                return false;
            }
        }


        private IPAddress PoimiKoneenIP()
        {
            //Poimitaan koneen oma ip-osoite
            IPEndPoint endPoint;
            using (Socket tempSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                tempSocket.Connect("10.2.3.4", 65530);
                endPoint = tempSocket.LocalEndPoint as IPEndPoint;
                Console.WriteLine(endPoint.Address.ToString());
            }
            return endPoint.Address;
        }

        /// <summary>
        /// Luodaan viestin kuuntelija -säie.
        /// </summary>
        private void LuoThread()
        {
            sr = new StreamReader(ns, System.Text.Encoding.ASCII);
            askuuntele = new AsiakasKuuntelee(sr);
            threadKuuntele = new Thread(new ThreadStart(askuuntele.Kuuntele));
            threadKuuntele.Start();
            askuuntele.Viesti += ViestiTullut;
        }


        /// <summary>
        /// String luonnolliseksi luvuksi.
        /// </summary>
        /// <param name="st">string</param>
        /// <returns>int (-1 jos muunnos ei onnistunut.)</returns>
        public int StringToInt(String st)
        {
            try
            {
                return UInt16.Parse(st);
            }
            catch (Exception)
            {
                return -1;
            }
        }


        /// <summary>
        /// Otetaan vastaan kuuntelijasäikeen lukema (eli toisen pelaajan lähettämä) viesti 
        /// ja toimitaan viestin perusteella.
        /// Huom. Pitäisi olla private eikä public
        /// </summary>
        /// <param name="msg">viesti</param>
        public void ViestiTullut(String msg)
        {
            String[] palat = msg.Split(' ');
            switch (state)
            {
                case "start":
                    switch (palat[0].ToUpper())
                    {
                        case "LOGOUT":
                            state = "wait";
                            if (LahtiPois != null) LahtiPois();
                            break;
                        case "JOIN":
                            if (palat.Length > 1)
                            {
                                if (ToinenPelaaja != null) ToinenPelaaja(palat[1]);
                            }
                            break;
                        default:
                            break;
                    }
                    break;

                case "setgame":
                    switch (palat[0].ToUpper())
                    {
                        case "LOGOUT":
                            state = "wait";
                            if (LahtiPois != null) LahtiPois();
                            break;
                        case "JOIN":
                            if (palat.Length > 1)
                            {
                                if (ToinenPelaaja != null) ToinenPelaaja(palat[1]);
                            }
                            break;
                        case "NEWGAME":
                            if (palat.Length > 2)
                            {
                                state = "startgame";
                                if (PeliAloitus != null) PeliAloitus(StringToInt(palat[1]), StringToInt(palat[2]));
                            }
                            break;
                        default:
                            break;
                    }
                    break;

                case "startgame":
                    switch (palat[0].ToUpper())
                    {
                        case "LOGOUT":
                            state = "wait";
                            if (LahtiPois != null) LahtiPois();
                            break;
                        case "CHAT":
                            if (palat.Length > 2)
                            {
                                if (Chattaa != null) Chattaa(msg.Substring(5));
                            }
                            break;
                        case "NEWGAME":
                            if (palat.Length > 2)
                            {
                                if (PeliAloitus != null) PeliAloitus(StringToInt(palat[1]), StringToInt(palat[2]));
                            }
                            break;
                        case "MOVEFROM":
                            if (palat.Length > 2)
                            {
                                rivi1 = StringToInt(palat[1]);
                                sarake1 = StringToInt(palat[2]);
                                state = "game";
                            }
                            break;
                        default:
                            break;
                    }
                    break;
                case "game":
                    switch (palat[0].ToUpper())
                    {
                        case "LOGOUT":
                            state = "wait";
                            if (LahtiPois != null) LahtiPois();
                            break;
                        case "CHAT":
                            if (palat.Length > 1)
                            {
                                if (Chattaa != null) Chattaa(msg.Substring(5));
                            }
                            break;
                        case "NEWGAME":
                            if (palat.Length > 2)
                            {
                                state = "startgame";
                                if (PeliAloitus != null) PeliAloitus(StringToInt(palat[1]), StringToInt(palat[2]));
                            }
                            break;
                        case "MOVEFROM":
                            if (palat.Length > 2)
                            {
                                rivi1 = StringToInt(palat[1]);
                                sarake1 = StringToInt(palat[2]);
                            }
                            break;
                        case "MOVETO":
                            if (palat.Length > 3)
                            {
                                bool p;
                                Boolean.TryParse(palat[3], out p);
                                if (MoveTo != null) MoveTo(rivi1, sarake1, StringToInt(palat[1]), StringToInt(palat[2]), p);
                            }
                            break;
                        default:
                            break;
                    }
                    break;
                default:
                    break;
            }
        }


        /// <summary>
        /// Toisen pelaajan puuttuessa aletaan kuunnella yhteydenottoja.
        /// </summary>
        public void StartListen()
        {
            listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //IPAddress ip = PoimiKoneenIP();
            IPEndPoint iep = new IPEndPoint(omaIP, PORT);
            listener.Bind(iep);
            listener.Listen(5);
            socket = listener.Accept();
            //Pelaajan saapuessa aiheutetaan toinen pelaaja saapui tapahtuma
            if (socket.Connected)
            {
                ns = new NetworkStream(socket);
                sw = new StreamWriter(ns, System.Text.Encoding.ASCII);
                LuoThread();
                state = "start";
            }
        }


        /// <summary>
        /// Liitytään peliin lähettämällä palvelimelle JOIN-viesti.
        /// </summary>
        /// <param name="nimi">pelaajan nimi</param>
        public void LiityPeliin(string nimi)
        {
            if (state.Equals("start")) 
            {
                SendMsg("JOIN " + nimi);
                state = "setgame";
            }
        }


        /// <summary>
        /// Lähetetään viesti toiselle pelaajalle.
        /// </summary>
        /// <param name="msg">viesti</param>
        private void SendMsg(string msg)
        {
            if (socket.Connected)
            {
                sw.WriteLine(msg);
                sw.Flush();
            }
        }


        /// <summary>
        /// Suljetaan yhteys ja kaikki streamit ja lisäthreadit.
        /// </summary>
        public void CloseConnection()
        {
            if (socket.Connected)
            {
                if (!state.Equals("wait")) SendMsg("LOGOUT");
                state = "wait";
                sw.Close();
                sr.Close();
                ns.Close();
                threadKuuntele.Abort();
                threadKuuntele.Join();
                socket.Close(100);
            }
            if (listener != null) listener.Close(100);
        }


        /// <summary>
        /// Lähetetään nappula-valittu viesti.
        /// </summary>
        /// <param name="x">nappulan rivi</param>
        /// <param name="y">nappulan sarake</param>
        public void NappulaValittu(int x, int y)
        {
            state = "game";
            SendMsg("MOVEFROM " + x + " " + y);
        }


        /// <summary>
        /// Lähetetään mihin ruutuun siirretään -viesti.
        /// </summary>
        /// <param name="rivi">ruudun rivi</param>
        /// <param name="sarake">ruudun sarake</param>
        /// <param name="syodaanko">syödäänkö nappula siirron yhteydessä</param>
        public void Siirra(int rivi, int sarake, bool syodaanko)
        {
            SendMsg("MOVETO " + rivi + " " + sarake + " " + syodaanko.ToString());
        }


        /// <summary>
        /// Uusi peli -viesti.
        /// </summary>
        /// <param name="lkm">pelilauden koko</param>
        /// <param name="p">aloittava pelaaja</param>
        public void UusiPeli(int lkm, int p)
        {
            state = "startgame";
            SendMsg("NEWGAME " + lkm + " " + p);
        }


        /// <summary>
        /// Lähetetään chat-viesti toiselle pelaajalle.
        /// </summary>
        /// <param name="msg">viesti</param>
        public void Chat(string msg)
        {
            SendMsg("CHAT " + msg);
        }
    }


    /// <summary>
    /// Pelaajan lähettämien viestien kuuntelija.
    /// </summary>
    public class AsiakasKuuntelee
    {
        StreamReader stream;

        public AsiakasKuuntelee(StreamReader sr)
        {
            stream = sr;
        }

        //Viestitään viestistä
        public delegate void ViestiSaapui(string pnimi);
        public event ViestiSaapui Viesti;

        /// <summary>
        /// Kuunnellaan saapuvia viestejä.
        /// </summary>
        public void Kuuntele()
        {
            while (true)
            {
                try
                {
                    String viesti = stream.ReadLine();
                    Console.WriteLine("Sain viestin: " + viesti);
                    if (Viesti != null) Viesti(viesti);
                }
                catch (Exception)
                {
                    Console.WriteLine("Ei tullut viestiä");
                }
                Thread.Sleep(10);
            }
        }
    }
}
