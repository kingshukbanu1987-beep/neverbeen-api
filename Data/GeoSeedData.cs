using System.Text.Json;

namespace NeverBeen.API.Data;

/// <summary>
/// Seed data for the Country drop-down and the City drop-down (cities are loaded
/// based on the selected country). The list is embedded as JSON so it can be extended
/// without code changes - just add more entries to <see cref="GeoJson"/>.
/// </summary>
public static class GeoSeedData
{
    private static readonly Lazy<IReadOnlyList<CountrySeed>> Data = new(Load);

    public static IReadOnlyList<CountrySeed> GetCountries() => Data.Value;

    public class CountrySeed
    {
        public string IsoCode2 { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? PhoneCode { get; set; }
        public List<string> Cities { get; set; } = new();
    }

    private static List<CountrySeed> Load()
    {
        using var doc = JsonDocument.Parse(GeoJson);
        var list = new List<CountrySeed>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var cities = new List<string>();
            if (element.TryGetProperty("cities", out var cityArray) && cityArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var city in cityArray.EnumerateArray())
                {
                    var name = city.GetString();
                    if (!string.IsNullOrWhiteSpace(name))
                        cities.Add(name);
                }
            }

            list.Add(new CountrySeed
            {
                IsoCode2 = element.GetProperty("iso2").GetString() ?? string.Empty,
                Name = element.GetProperty("name").GetString() ?? string.Empty,
                PhoneCode = element.TryGetProperty("phone", out var phone) ? phone.GetString() : null,
                Cities = cities
            });
        }
        return list;
    }

    private const string GeoJson = """
    [
      { "iso2": "AD", "name": "Andorra", "phone": "+376", "cities": ["Andorra la Vella", "Encamp", "La Massana"] },
      { "iso2": "AE", "name": "United Arab Emirates", "phone": "+971", "cities": ["Abu Dhabi", "Dubai", "Sharjah", "Ajman", "Al Ain", "Fujairah", "Ras Al Khaimah", "Umm Al Quwain"] },
      { "iso2": "AF", "name": "Afghanistan", "phone": "+93", "cities": ["Kabul", "Kandahar", "Herat", "Mazar-i-Sharif", "Jalalabad"] },
      { "iso2": "AG", "name": "Antigua and Barbuda", "phone": "+1", "cities": ["Saint John's", "Bolans"] },
      { "iso2": "AL", "name": "Albania", "phone": "+355", "cities": ["Tirana", "Durres", "Vlore", "Shkoder", "Fier"] },
      { "iso2": "AM", "name": "Armenia", "phone": "+374", "cities": ["Yerevan", "Gyumri", "Vanadzor", "Hrazdan"] },
      { "iso2": "AO", "name": "Angola", "phone": "+244", "cities": ["Luanda", "Huambo", "Lobito", "Benguela", "Namibe"] },
      { "iso2": "AR", "name": "Argentina", "phone": "+54", "cities": ["Buenos Aires", "Cordoba", "Rosario", "Mendoza", "Mar del Plata", "La Plata", "San Miguel de Tucuman", "Salta", "Neuquen"] },
      { "iso2": "AT", "name": "Austria", "phone": "+43", "cities": ["Vienna", "Graz", "Linz", "Salzburg", "Innsbruck", "Klagenfurt", "St. Polten"] },
      { "iso2": "AU", "name": "Australia", "phone": "+61", "cities": ["Sydney", "Melbourne", "Brisbane", "Perth", "Adelaide", "Canberra", "Newcastle", "Gold Coast", "Hobart", "Darwin", "Cairns", "Ballarat"] },
      { "iso2": "AZ", "name": "Azerbaijan", "phone": "+994", "cities": ["Baku", "Sumqayit", "Ganja", "Mingachevir"] },
      { "iso2": "BA", "name": "Bosnia and Herzegovina", "phone": "+387", "cities": ["Sarajevo", "Banja Luka", "Tuzla", "Zenica", "Mostar"] },
      { "iso2": "BB", "name": "Barbados", "phone": "+1", "cities": ["Bridgetown", "Speightstown"] },
      { "iso2": "BD", "name": "Bangladesh", "phone": "+880", "cities": ["Dhaka", "Chittagong", "Khulna", "Rajshahi", "Sylhet", "Barishal", "Narayanganj", "Gazipur", "Comilla"] },
      { "iso2": "BE", "name": "Belgium", "phone": "+32", "cities": ["Brussels", "Antwerp", "Ghent", "Liege", "Bruges", "Charleroi", "Namur"] },
      { "iso2": "BF", "name": "Burkina Faso", "phone": "+226", "cities": ["Ouagadougou", "Bobo-Dioulasso", "Koudougou"] },
      { "iso2": "BG", "name": "Bulgaria", "phone": "+359", "cities": ["Sofia", "Plovdiv", "Varna", "Burgas", "Stara Zagora", "Ruse"] },
      { "iso2": "BH", "name": "Bahrain", "phone": "+973", "cities": ["Manama", "Muharraq", "Riffa"] },
      { "iso2": "BI", "name": "Burundi", "phone": "+257", "cities": ["Bujumbura", "Gitega", "Ngozi"] },
      { "iso2": "BJ", "name": "Benin", "phone": "+229", "cities": ["Cotonou", "Porto-Novo", "Parakou", "Abomey"] },
      { "iso2": "BL", "name": "Saint Barthelemy", "phone": "+590", "cities": ["Gustavia"] },
      { "iso2": "BM", "name": "Bermuda", "phone": "+1", "cities": ["Hamilton", "Paget", "St. George's"] },
      { "iso2": "BN", "name": "Brunei", "phone": "+673", "cities": ["Bandar Seri Begawan", "Seria", "Kuala Belait"] },
      { "iso2": "BO", "name": "Bolivia", "phone": "+591", "cities": ["La Paz", "Santa Cruz de la Sierra", "Cochabamba", "Sucre", "Oruro"] },
      { "iso2": "BR", "name": "Brazil", "phone": "+55", "cities": ["Sao Paulo", "Rio de Janeiro", "Brasilia", "Salvador", "Fortaleza", "Belo Horizonte", "Curitiba", "Recife", "Manaus", "Goiania", "Belem", "Porto Alegre"] },
      { "iso2": "BS", "name": "Bahamas", "phone": "+1", "cities": ["Nassau", "Freeport", "George Town"] },
      { "iso2": "BT", "name": "Bhutan", "phone": "+975", "cities": ["Thimphu", "Paro", "Punakha", "Gelephu"] },
      { "iso2": "BW", "name": "Botswana", "phone": "+267", "cities": ["Gaborone", "Francistown", "Maun", "Molepolole", "Mochudi"] },
      { "iso2": "BY", "name": "Belarus", "phone": "+375", "cities": ["Minsk", "Gomel", "Brest", "Vitebsk", "Grodno"] },
      { "iso2": "BZ", "name": "Belize", "phone": "+501", "cities": ["Belmopan", "Belize City", "San Ignacio"] },
      { "iso2": "CA", "name": "Canada", "phone": "+1", "cities": ["Toronto", "Montreal", "Vancouver", "Calgary", "Ottawa", "Edmonton", "Winnipeg", "Quebec City", "Hamilton", "Mississauga", "London", "Kitchener", "Markham", "Surrey", "Victoria", "Oshawa", "St. Catharines"] },
      { "iso2": "CD", "name": "Democratic Republic of the Congo", "phone": "+243", "cities": ["Kinshasa", "Lubumbashi", "Mbuji-Mayi", "Goma"] },
      { "iso2": "CF", "name": "Central African Republic", "phone": "+236", "cities": ["Bangui", "Bimbo", "Bouar"] },
      { "iso2": "CG", "name": "Republic of the Congo", "phone": "+242", "cities": ["Brazzaville", "Pointe-Noire", "Owando"] },
      { "iso2": "CH", "name": "Switzerland", "phone": "+41", "cities": ["Zurich", "Geneva", "Basel", "Lausanne", "Bern", "Lugano", "St. Gallen"] },
      { "iso2": "CI", "name": "Cote d'Ivoire", "phone": "+225", "cities": ["Abidjan", "Bouake", "Yamoussoukro", "Korhogo", "San-Pedro"] },
      { "iso2": "CL", "name": "Chile", "phone": "+56", "cities": ["Santiago", "Valparaiso", "Concepcion", "Antofagasta", "Puerto Montt", "Vina del Mar", "Temuco"] },
      { "iso2": "CM", "name": "Cameroon", "phone": "+237", "cities": ["Douala", "Yaounde", "Bamenda", "Garoua", "Maroua"] },
      { "iso2": "CN", "name": "China", "phone": "+86", "cities": ["Beijing", "Shanghai", "Guangzhou", "Shenzhen", "Chengdu", "Hangzhou", "Wuhan", "Nanjing", "Tianjin", "Chongqing", "Xi'an", "Suzhou", "Qingdao", "Dalian"] },
      { "iso2": "CO", "name": "Colombia", "phone": "+57", "cities": ["Bogota", "Medellin", "Cali", "Cartagena", "Barranquilla", "Bucaramanga", "Pereira"] },
      { "iso2": "CR", "name": "Costa Rica", "phone": "+506", "cities": ["San Jose", "Alajuela", "Heredia", "Limon", "Escazu"] },
      { "iso2": "CU", "name": "Cuba", "phone": "+53", "cities": ["Havana", "Santiago de Cuba", "Camaguey", "Holguin", "Varadero"] },
      { "iso2": "CV", "name": "Cape Verde", "phone": "+238", "cities": ["Praia", "Mindelo"] },
      { "iso2": "CY", "name": "Cyprus", "phone": "+357", "cities": ["Nicosia", "Limassol", "Paphos", "Larnaca", "Famagusta"] },
      { "iso2": "CZ", "name": "Czech Republic", "phone": "+420", "cities": ["Prague", "Brno", "Ostrava", "Plzen", "Olomouc", "Liberec"] },
      { "iso2": "DE", "name": "Germany", "phone": "+49", "cities": ["Berlin", "Munich", "Hamburg", "Cologne", "Frankfurt am Main", "Stuttgart", "Dusseldorf", "Leipzig", "Dortmund", "Essen", "Bremen", "Dresden", "Nuremberg", "Hanover"] },
      { "iso2": "DJ", "name": "Djibouti", "phone": "+253", "cities": ["Djibouti City", "Tadjoura"] },
      { "iso2": "DK", "name": "Denmark", "phone": "+45", "cities": ["Copenhagen", "Aarhus", "Odense", "Aalborg", "Esbjerg"] },
      { "iso2": "DM", "name": "Dominica", "phone": "+1", "cities": ["Roseau", "Saint Joseph"] },
      { "iso2": "DO", "name": "Dominican Republic", "phone": "+1", "cities": ["Santo Domingo", "Santiago de los Caballeros", "La Romana", "Puerto Plata"] },
      { "iso2": "DZ", "name": "Algeria", "phone": "+213", "cities": ["Algiers", "Oran", "Constantine", "Annaba", "Blida"] },
      { "iso2": "EC", "name": "Ecuador", "phone": "+593", "cities": ["Quito", "Guayaquil", "Cuenca", "Ambato", "Riobamba"] },
      { "iso2": "EE", "name": "Estonia", "phone": "+372", "cities": ["Tallinn", "Tartu", "Parnu", "Narva"] },
      { "iso2": "EG", "name": "Egypt", "phone": "+20", "cities": ["Cairo", "Alexandria", "Giza", "Luxor", "Aswan", "Mansoura", "Tanta"] },
      { "iso2": "ER", "name": "Eritrea", "phone": "+291", "cities": ["Asmara", "Keren", "Massawa"] },
      { "iso2": "ES", "name": "Spain", "phone": "+34", "cities": ["Madrid", "Barcelona", "Valencia", "Seville", "Bilbao", "Malaga", "Zaragoza", "Murcia", "Palma de Mallorca", "Alicante", "Valladolid"] },
      { "iso2": "ET", "name": "Ethiopia", "phone": "+251", "cities": ["Addis Ababa", "Dire Dawa", "Mekele", "Hawassa"] },
      { "iso2": "FJ", "name": "Fiji", "phone": "+679", "cities": ["Suva", "Nadi", "Lautoka"] },
      { "iso2": "FI", "name": "Finland", "phone": "+358", "cities": ["Helsinki", "Espoo", "Tampere", "Turku", "Oulu", "Lahti"] },
      { "iso2": "FR", "name": "France", "phone": "+33", "cities": ["Paris", "Marseille", "Lyon", "Toulouse", "Nice", "Nantes", "Strasbourg", "Montpellier", "Bordeaux", "Lille", "Rennes", "Saint-Etienne"] },
      { "iso2": "GA", "name": "Gabon", "phone": "+241", "cities": ["Libreville", "Port-Gentil", "Franceville"] },
      { "iso2": "GB", "name": "United Kingdom", "phone": "+44", "cities": ["London", "Manchester", "Birmingham", "Glasgow", "Liverpool", "Leeds", "Bristol", "Sheffield", "Edinburgh", "Newcastle upon Tyne", "Nottingham", "Leicester", "Cardiff", "Belfast", "Brighton", "Aberdeen", "Southampton", "Derby", "Coventry", "Wolverhampton", "Norwich", "Exeter", "Reading", "Swansea"] },
      { "iso2": "GE", "name": "Georgia", "phone": "+995", "cities": ["Tbilisi", "Batumi", "Kutaisi", "Rustavi"] },
      { "iso2": "GH", "name": "Ghana", "phone": "+233", "cities": ["Accra", "Kumasi", "Takoradi", "Tamale", "Cape Coast", "Koforidua"] },
      { "iso2": "GD", "name": "Grenada", "phone": "+1", "cities": ["St. George's", "Gouyave"] },
      { "iso2": "GN", "name": "Guinea", "phone": "+224", "cities": ["Conakry", "Kankan", "Labe"] },
      { "iso2": "GQ", "name": "Equatorial Guinea", "phone": "+240", "cities": ["Malabo", "Bata"] },
      { "iso2": "GR", "name": "Greece", "phone": "+30", "cities": ["Athens", "Thessaloniki", "Patras", "Heraklion", "Larissa", "Volos"] },
      { "iso2": "GT", "name": "Guatemala", "phone": "+502", "cities": ["Guatemala City", "Quetzaltenango", "Antigua", "Escuintla"] },
      { "iso2": "GM", "name": "Gambia", "phone": "+220", "cities": ["Banjul", "Serekunda", "Brikama"] },
      { "iso2": "GW", "name": "Guinea-Bissau", "phone": "+245", "cities": ["Bissau", "Bafata"] },
      { "iso2": "GY", "name": "Guyana", "phone": "+592", "cities": ["Georgetown", "Linden", "Corriverton"] },
      { "iso2": "HK", "name": "Hong Kong", "phone": "+852", "cities": ["Hong Kong Island", "Kowloon", "New Territories"] },
      { "iso2": "HN", "name": "Honduras", "phone": "+504", "cities": ["Tegucigalpa", "San Pedro Sula", "La Ceiba", "Choluteca"] },
      { "iso2": "HR", "name": "Croatia", "phone": "+385", "cities": ["Zagreb", "Split", "Dubrovnik", "Rijeka", "Osijek", "Zadar"] },
      { "iso2": "HT", "name": "Haiti", "phone": "+509", "cities": ["Port-au-Prince", "Cap-Haitien", "Gonaives"] },
      { "iso2": "HU", "name": "Hungary", "phone": "+36", "cities": ["Budapest", "Debrecen", "Szeged", "Miskolc", "Pecs", "Gyor"] },
      { "iso2": "ID", "name": "Indonesia", "phone": "+62", "cities": ["Jakarta", "Surabaya", "Bandung", "Medan", "Semarang", "Makassar", "Denpasar", "Yogyakarta", "Palembang", "Bekasi"] },
      { "iso2": "IE", "name": "Ireland", "phone": "+353", "cities": ["Dublin", "Cork", "Galway", "Limerick", "Waterford", "Kilkenny"] },
      { "iso2": "IL", "name": "Israel", "phone": "+972", "cities": ["Jerusalem", "Tel Aviv", "Haifa", "Beersheba", "Rishon LeZion"] },
      { "iso2": "IN", "name": "India", "phone": "+91", "cities": ["Mumbai", "Delhi", "Bengaluru", "Hyderabad", "Chennai", "Kolkata", "Ahmedabad", "Pune", "Jaipur", "Surat", "Lucknow", "Kanpur", "Nagpur", "Indore", "Bhopal", "Ludhiana", "Vadodara", "Nashik", "Agra", "Navi Mumbai", "Patna", "Faridabad", "Meerut", "Kochi", "Thiruvananthapuram", "Guwahati", "Chandigarh", "Dehradun", "Coimbatore", "Madurai", "Visakhapatnam", "Rajkot", "Amritsar", "Varanasi", "Prayagraj", "Raipur", "Jodhpur", "Udaipur", "Mysuru", "Mangaluru"] },
      { "iso2": "IQ", "name": "Iraq", "phone": "+964", "cities": ["Baghdad", "Basra", "Erbil", "Mosul", "Najaf", "Kirkuk"] },
      { "iso2": "IR", "name": "Iran", "phone": "+98", "cities": ["Tehran", "Mashhad", "Isfahan", "Shiraz", "Tabriz", "Karaj", "Qom"] },
      { "iso2": "IS", "name": "Iceland", "phone": "+354", "cities": ["Reykjavik", "Keflavik", "Akureyri"] },
      { "iso2": "IT", "name": "Italy", "phone": "+39", "cities": ["Rome", "Milan", "Naples", "Turin", "Florence", "Bologna", "Venice", "Genoa", "Palermo", "Verona", "Bari", "Catania"] },
      { "iso2": "JM", "name": "Jamaica", "phone": "+1", "cities": ["Kingston", "Montego Bay", "Port Antonio", "Ocho Rios"] },
      { "iso2": "JO", "name": "Jordan", "phone": "+962", "cities": ["Amman", "Zarqa", "Irbid", "Aqaba", "Salt"] },
      { "iso2": "JP", "name": "Japan", "phone": "+81", "cities": ["Tokyo", "Osaka", "Yokohama", "Nagoya", "Sapporo", "Kobe", "Kyoto", "Fukuoka", "Sendai", "Hiroshima"] },
      { "iso2": "KE", "name": "Kenya", "phone": "+254", "cities": ["Nairobi", "Mombasa", "Kisumu", "Nakuru", "Eldoret", "Garissa"] },
      { "iso2": "KG", "name": "Kyrgyzstan", "phone": "+996", "cities": ["Bishkek", "Osh", "Naryn"] },
      { "iso2": "KH", "name": "Cambodia", "phone": "+855", "cities": ["Phnom Penh", "Siem Reap", "Sihanoukville", "Battambang"] },
      { "iso2": "KM", "name": "Comoros", "phone": "+269", "cities": ["Moroni", "Mutsamudu", "Fomboni"] },
      { "iso2": "KP", "name": "North Korea", "phone": "+850", "cities": ["Pyongyang", "Hamhung", "Chongjin", "Wonsan"] },
      { "iso2": "KR", "name": "South Korea", "phone": "+82", "cities": ["Seoul", "Busan", "Incheon", "Daegu", "Daejeon", "Gwangju", "Ulsan"] },
      { "iso2": "KW", "name": "Kuwait", "phone": "+965", "cities": ["Kuwait City", "Hawalli", "Salmiya"] },
      { "iso2": "KZ", "name": "Kazakhstan", "phone": "+7", "cities": ["Astana", "Almaty", "Shymkent", "Karaganda", "Atyrau"] },
      { "iso2": "LA", "name": "Laos", "phone": "+856", "cities": ["Vientiane", "Luang Prabang", "Pakse", "Savannakhet"] },
      { "iso2": "LB", "name": "Lebanon", "phone": "+961", "cities": ["Beirut", "Tripoli", "Sidon", "Tyre", "Zahle"] },
      { "iso2": "LI", "name": "Liechtenstein", "phone": "+423", "cities": ["Vaduz", "Schaan", "Balzers"] },
      { "iso2": "LK", "name": "Sri Lanka", "phone": "+94", "cities": ["Colombo", "Kandy", "Galle", "Negombo", "Jaffna", "Gampaha", "Kurunegala"] },
      { "iso2": "LR", "name": "Liberia", "phone": "+231", "cities": ["Monrovia", "Gbarnga", "Buchanan"] },
      { "iso2": "LS", "name": "Lesotho", "phone": "+266", "cities": ["Maseru", "Hlotse", "Teyateyaneng"] },
      { "iso2": "LT", "name": "Lithuania", "phone": "+370", "cities": ["Vilnius", "Kaunas", "Klaipeda", "Panevezys"] },
      { "iso2": "LU", "name": "Luxembourg", "phone": "+352", "cities": ["Luxembourg City", "Esch-sur-Alzette", "Differdange"] },
      { "iso2": "LV", "name": "Latvia", "phone": "+371", "cities": ["Riga", "Daugavpils", "Liepaja", "Jelgava"] },
      { "iso2": "LY", "name": "Libya", "phone": "+218", "cities": ["Tripoli", "Benghazi", "Misrata", "Zawiya"] },
      { "iso2": "MA", "name": "Morocco", "phone": "+212", "cities": ["Casablanca", "Rabat", "Marrakesh", "Fez", "Tangier", "Agadir", "Meknes"] },
      { "iso2": "MC", "name": "Monaco", "phone": "+377", "cities": ["Monaco", "Monte-Carlo", "La Condamine"] },
      { "iso2": "MD", "name": "Moldova", "phone": "+373", "cities": ["Chisinau", "Balti", "Benders", "Tiraspol"] },
      { "iso2": "ME", "name": "Montenegro", "phone": "+382", "cities": ["Podgorica", "Niksic", "Bar", "Herceg Novi"] },
      { "iso2": "MG", "name": "Madagascar", "phone": "+261", "cities": ["Antananarivo", "Toamasina", "Mahajanga", "Toliara"] },
      { "iso2": "MK", "name": "North Macedonia", "phone": "+389", "cities": ["Skopje", "Bitola", "Kumanovo"] },
      { "iso2": "ML", "name": "Mali", "phone": "+223", "cities": ["Bamako", "Segou", "Sikasso"] },
      { "iso2": "MM", "name": "Myanmar", "phone": "+95", "cities": ["Yangon", "Mandalay", "Naypyitaw", "Bago", "Mawlamyine"] },
      { "iso2": "MN", "name": "Mongolia", "phone": "+976", "cities": ["Ulaanbaatar", "Erdenet", "Darhan", "Choibalsan"] },
      { "iso2": "MR", "name": "Mauritania", "phone": "+222", "cities": ["Nouakchott", "Nouadhibou", "Rosso"] },
      { "iso2": "MT", "name": "Malta", "phone": "+356", "cities": ["Valletta", "Sliema", "Birkirkara", "Mosta"] },
      { "iso2": "MV", "name": "Maldives", "phone": "+960", "cities": ["Male", "Addu City"] },
      { "iso2": "MW", "name": "Malawi", "phone": "+265", "cities": ["Lilongwe", "Blantyre", "Mzuzu", "Zomba"] },
      { "iso2": "MX", "name": "Mexico", "phone": "+52", "cities": ["Mexico City", "Guadalajara", "Monterrey", "Puebla", "Tijuana", "Cancun", "Merida", "Queretaro", "Toluca", "Tampico"] },
      { "iso2": "MY", "name": "Malaysia", "phone": "+60", "cities": ["Kuala Lumpur", "George Town", "Johor Bahru", "Ipoh", "Shah Alam", "Kuantan", "Kota Kinabalu", "Kuching"] },
      { "iso2": "MZ", "name": "Mozambique", "phone": "+258", "cities": ["Maputo", "Beira", "Nampula", "Matola", "Chimoio"] },
      { "iso2": "NA", "name": "Namibia", "phone": "+264", "cities": ["Windhoek", "Walvis Bay", "Swakopmund", "Oshakati"] },
      { "iso2": "NE", "name": "Niger", "phone": "+227", "cities": ["Niamey", "Zinder", "Maradi", "Dosso"] },
      { "iso2": "NG", "name": "Nigeria", "phone": "+234", "cities": ["Lagos", "Abuja", "Kano", "Ibadan", "Port Harcourt", "Benin City", "Enugu", "Kaduna"] },
      { "iso2": "NI", "name": "Nicaragua", "phone": "+505", "cities": ["Managua", "Leon", "Granada", "Masaya"] },
      { "iso2": "NL", "name": "Netherlands", "phone": "+31", "cities": ["Amsterdam", "Rotterdam", "The Hague", "Utrecht", "Eindhoven", "Groningen", "Tilburg", "Almere"] },
      { "iso2": "NO", "name": "Norway", "phone": "+47", "cities": ["Oslo", "Bergen", "Trondheim", "Stavanger", "Tromso", "Alesund"] },
      { "iso2": "NP", "name": "Nepal", "phone": "+977", "cities": ["Kathmandu", "Pokhara", "Lalitpur", "Bhaktapur", "Biratnagar", "Butwal"] },
      { "iso2": "NZ", "name": "New Zealand", "phone": "+64", "cities": ["Auckland", "Wellington", "Christchurch", "Hamilton", "Dunedin", "Napier", "Tauranga"] },
      { "iso2": "OM", "name": "Oman", "phone": "+968", "cities": ["Muscat", "Salalah", "Sohar", "Nizwa", "Sur"] },
      { "iso2": "PA", "name": "Panama", "phone": "+507", "cities": ["Panama City", "David", "Colon", "Boquete"] },
      { "iso2": "PE", "name": "Peru", "phone": "+51", "cities": ["Lima", "Arequipa", "Cusco", "Trujillo", "Chiclayo", "Piura", "Iquitos"] },
      { "iso2": "PG", "name": "Papua New Guinea", "phone": "+675", "cities": ["Port Moresby", "Lae", "Madang", "Goroka"] },
      { "iso2": "PH", "name": "Philippines", "phone": "+63", "cities": ["Manila", "Quezon City", "Cebu City", "Davao City", "Bacoor", "Muntinlupa", "Baguio"] },
      { "iso2": "PK", "name": "Pakistan", "phone": "+92", "cities": ["Karachi", "Lahore", "Islamabad", "Rawalpindi", "Faisalabad", "Peshawar", "Multan", "Quetta", "Hyderabad", "Sialkot", "Gujranwala"] },
      { "iso2": "PL", "name": "Poland", "phone": "+48", "cities": ["Warsaw", "Krakow", "Lodz", "Wroclaw", "Poznan", "Gdansk", "Katowice", "Szczecin", "Lublin"] },
      { "iso2": "PT", "name": "Portugal", "phone": "+351", "cities": ["Lisbon", "Porto", "Coimbra", "Braga", "Faro", "Setubal"] },
      { "iso2": "PY", "name": "Paraguay", "phone": "+595", "cities": ["Asuncion", "Ciudad del Este", "Encarnacion", "Concepcion"] },
      { "iso2": "QA", "name": "Qatar", "phone": "+974", "cities": ["Doha", "Al Rayyan", "Al Wakrah", "Al Khor"] },
      { "iso2": "RO", "name": "Romania", "phone": "+40", "cities": ["Bucharest", "Cluj-Napoca", "Timisoara", "Iasi", "Constanta", "Brasov", "Craiova"] },
      { "iso2": "RS", "name": "Serbia", "phone": "+381", "cities": ["Belgrade", "Novi Sad", "Nis", "Kragujevac", "Subotica"] },
      { "iso2": "RU", "name": "Russia", "phone": "+7", "cities": ["Moscow", "Saint Petersburg", "Novosibirsk", "Yekaterinburg", "Kazan", "Nizhny Novgorod", "Samara", "Rostov-on-Don"] },
      { "iso2": "RW", "name": "Rwanda", "phone": "+256", "cities": ["Kigali", "Butare", "Gitarama", "Rwamagana"] },
      { "iso2": "SA", "name": "Saudi Arabia", "phone": "+966", "cities": ["Riyadh", "Jeddah", "Mecca", "Medina", "Dammam", "Khobar", "Taif"] },
      { "iso2": "SC", "name": "Seychelles", "phone": "+248", "cities": ["Victoria", "Bel Ombre"] },
      { "iso2": "SD", "name": "Sudan", "phone": "+249", "cities": ["Khartoum", "Omdurman", "Port Sudan", "Kassala"] },
      { "iso2": "SE", "name": "Sweden", "phone": "+46", "cities": ["Stockholm", "Gothenburg", "Malmo", "Uppsala", "Linkoping", "Sundsvall"] },
      { "iso2": "SG", "name": "Singapore", "phone": "+65", "cities": ["Singapore"] },
      { "iso2": "SI", "name": "Slovenia", "phone": "+386", "cities": ["Ljubljana", "Maribor", "Celje", "Koper"] },
      { "iso2": "SK", "name": "Slovakia", "phone": "+421", "cities": ["Bratislava", "Kosice", "Nitra", "Zilina", "Presov"] },
      { "iso2": "SL", "name": "Sierra Leone", "phone": "+232", "cities": ["Freetown", "Bo", "Kenema"] },
      { "iso2": "SN", "name": "Senegal", "phone": "+221", "cities": ["Dakar", "Thies", "Saint-Louis", "Ziguinchor"] },
      { "iso2": "SO", "name": "Somalia", "phone": "+252", "cities": ["Mogadishu", "Hargeisa", "Kismayo", "Bosaso"] },
      { "iso2": "SR", "name": "Suriname", "phone": "+597", "cities": ["Paramaribo", "Nickerie", "Lelydorp"] },
      { "iso2": "SS", "name": "South Sudan", "phone": "+211", "cities": ["Juba", "Malakal", "Wau"] },
      { "iso2": "SV", "name": "El Salvador", "phone": "+503", "cities": ["San Salvador", "Santa Ana", "San Miguel", "Sonsonate"] },
      { "iso2": "SZ", "name": "Eswatini", "phone": "+268", "cities": ["Mbabane", "Lobamba", "Manzini"] },
      { "iso2": "SY", "name": "Syria", "phone": "+963", "cities": ["Damascus", "Aleppo", "Homs", "Latakia", "Hama"] },
      { "iso2": "TD", "name": "Chad", "phone": "+235", "cities": ["N'Djamena", "Moundou", "Sarh"] },
      { "iso2": "TG", "name": "Togo", "phone": "+228", "cities": ["Lome", "Sokode", "Kpalime"] },
      { "iso2": "TH", "name": "Thailand", "phone": "+66", "cities": ["Bangkok", "Chiang Mai", "Phuket", "Pattaya", "Songkhla", "Nakhon Ratchasima", "Khon Kaen"] },
      { "iso2": "TJ", "name": "Tajikistan", "phone": "+992", "cities": ["Dushanbe", "Khujand", "Kulob"] },
      { "iso2": "TL", "name": "Timor-Leste", "phone": "+670", "cities": ["Dili", "Baucau", "Suai"] },
      { "iso2": "TM", "name": "Turkmenistan", "phone": "+993", "cities": ["Ashgabat", "Mary", "Turkmenabat"] },
      { "iso2": "TN", "name": "Tunisia", "phone": "+216", "cities": ["Tunis", "Sfax", "Sousse", "Bizerte", "Gabes"] },
      { "iso2": "TO", "name": "Tonga", "phone": "+676", "cities": ["Nuku'alofa", "Neiafu"] },
      { "iso2": "TR", "name": "Turkey", "phone": "+90", "cities": ["Istanbul", "Ankara", "Izmir", "Bursa", "Antalya", "Adana", "Konya", "Gaziantep"] },
      { "iso2": "TT", "name": "Trinidad and Tobago", "phone": "+1", "cities": ["Port of Spain", "Chaguanas", "Arima"] },
      { "iso2": "TW", "name": "Taiwan", "phone": "+886", "cities": ["Taipei", "Kaohsiung", "Taichung", "New Taipei", "Taoyuan"] },
      { "iso2": "TZ", "name": "Tanzania", "phone": "+255", "cities": ["Dar es Salaam", "Arusha", "Mwanza", "Dodoma", "Zanzibar City", "Morogoro"] },
      { "iso2": "UA", "name": "Ukraine", "phone": "+380", "cities": ["Kyiv", "Kharkiv", "Odesa", "Lviv", "Dnipro", "Vinnytsia", "Poltava"] },
      { "iso2": "UG", "name": "Uganda", "phone": "+256", "cities": ["Kampala", "Entebbe", "Jinja", "Gulu", "Mbarara"] },
      { "iso2": "US", "name": "United States", "phone": "+1", "cities": ["New York", "Los Angeles", "Chicago", "Houston", "Phoenix", "Philadelphia", "San Antonio", "San Diego", "Dallas", "Austin", "Jacksonville", "Fort Worth", "San Jose", "Columbus", "Indianapolis", "Charlotte", "San Francisco", "Seattle", "Denver", "Washington", "Boston", "Detroit", "Nashville", "El Paso", "Portland", "Memphis", "Las Vegas", "Louisville", "Baltimore", "Oklahoma City", "Miami", "Atlanta", "New Orleans", "Raleigh", "Tucson"] },
      { "iso2": "UY", "name": "Uruguay", "phone": "+598", "cities": ["Montevideo", "Salto", "Paysandu", "Punta del Este"] },
      { "iso2": "UZ", "name": "Uzbekistan", "phone": "+998", "cities": ["Tashkent", "Samarkand", "Bukhara", "Nukus", "Fergana"] },
      { "iso2": "VE", "name": "Venezuela", "phone": "+58", "cities": ["Caracas", "Maracaibo", "Valencia", "Barquisimeto", "Ciudad Guayana"] },
      { "iso2": "VN", "name": "Vietnam", "phone": "+84", "cities": ["Hanoi", "Ho Chi Minh City", "Da Nang", "Hai Phong", "Can Tho", "Hue"] },
      { "iso2": "VU", "name": "Vanuatu", "phone": "+678", "cities": ["Port Vila", "Luganville", "Tanna"] },
      { "iso2": "WS", "name": "Samoa", "phone": "+685", "cities": ["Apia", "Vailele", "Salelologa"] },
      { "iso2": "YE", "name": "Yemen", "phone": "+967", "cities": ["Sanaa", "Aden", "Taiz", "Hodeidah", "Mukalla"] },
      { "iso2": "ZA", "name": "South Africa", "phone": "+27", "cities": ["Johannesburg", "Cape Town", "Durban", "Pretoria", "Port Elizabeth", "Bloemfontein", "Pietermaritzburg"] },
      { "iso2": "ZM", "name": "Zambia", "phone": "+260", "cities": ["Lusaka", "Ndola", "Kitwe", "Livingstone"] },
      { "iso2": "ZW", "name": "Zimbabwe", "phone": "+263", "cities": ["Harare", "Bulawayo", "Mutare", "Gweru"] }
    ]
    """;
}
