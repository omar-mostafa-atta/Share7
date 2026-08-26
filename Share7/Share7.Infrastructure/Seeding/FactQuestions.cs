namespace Share7.Infrastructure.Seeding;

/// <summary>
/// One curated fact, authored in both content languages.
/// <para>
/// Both languages live on one record on purpose. A lesson's English and Arabic question sets are
/// stored separately and versioned separately — that is the schema — but they should still be
/// <i>about the same thing</i>, and two parallel arrays would drift the first time somebody edited
/// one of them.
/// </para>
/// </summary>
internal readonly record struct Fact(
    string TextEn, string CorrectEn, string WrongAEn, string WrongBEn,
    string TextAr, string CorrectAr, string WrongAAr, string WrongBAr);

/// <summary>
/// The curated half of the question content: science, English, Arabic and social studies, banded by
/// grade stage.
/// <para>
/// <b>Curated rather than generated, because these subjects have no generator.</b> There is no
/// function that emits a true sentence about photosynthesis the way one emits a true sum. So these
/// are written out, banded to the stage that teaches them, and cycled across lessons by an offset
/// derived from the lesson's own position — which gives a different set of questions in each lesson
/// of a chapter rather than the same five everywhere.
/// </para>
/// <para>
/// <b>These are placeholder-quality content, not a syllabus.</b> They are real questions with real
/// answers — a child can answer them and be right or wrong — which is what makes the game playable
/// end to end. They are not aligned to any ministry scheme of work, and a bank this size repeats
/// across a fifteen-hundred-lesson tree. Replacing a lesson's set with an authored sheet is an
/// upload, and the seeder leaves any lesson that already has questions alone.
/// </para>
/// </summary>
internal static class FactQuestions
{
    public static SeedQuestion For(string subjectKey, int gradeOrder, bool arabic, int stream)
    {
        var bank = BankFor(subjectKey, gradeOrder);
        var s = stream < 0 ? -stream : stream;
        var fact = bank[s % bank.Length];

        return arabic
            ? new SeedQuestion(fact.TextAr, fact.CorrectAr, fact.WrongAAr, fact.WrongBAr)
            : new SeedQuestion(fact.TextEn, fact.CorrectEn, fact.WrongAEn, fact.WrongBEn);
    }

    private static Fact[] BankFor(string subjectKey, int gradeOrder) => subjectKey switch
    {
        SubjectKeys.Science => gradeOrder switch
        {
            <= 2 => ScienceKindergarten,
            <= 8 => SciencePrimary,
            <= 11 => SciencePreparatory,
            _ => ScienceSecondary
        },
        SubjectKeys.English => gradeOrder switch
        {
            <= 2 => EnglishKindergarten,
            <= 8 => EnglishPrimary,
            <= 11 => EnglishPreparatory,
            _ => EnglishSecondary
        },
        SubjectKeys.Arabic => gradeOrder switch
        {
            <= 2 => ArabicKindergarten,
            <= 8 => ArabicPrimary,
            <= 11 => ArabicPreparatory,
            _ => ArabicSecondary
        },
        _ => SocialStudies
    };

    // ── Science ───────────────────────────────────────────────────────────────

    private static readonly Fact[] ScienceKindergarten =
    [
        new("Which animal says \"meow\"?", "A cat", "A dog", "A cow",
            "ما الحيوان الذي يقول \"مواء\"؟", "القطة", "الكلب", "البقرة"),
        new("What do we use to see?", "Our eyes", "Our ears", "Our nose",
            "بماذا نرى؟", "بالعينين", "بالأذنين", "بالأنف"),
        new("Which one is a fruit?", "An apple", "A carrot", "A potato",
            "أي مما يلي فاكهة؟", "التفاح", "الجزر", "البطاطس"),
        new("What colour is a banana?", "Yellow", "Blue", "Purple",
            "ما لون الموزة؟", "أصفر", "أزرق", "بنفسجي"),
        new("Where do fish live?", "In water", "In trees", "In sand",
            "أين تعيش الأسماك؟", "في الماء", "في الشجر", "في الرمل"),
        new("What do plants need to grow?", "Water", "Sand", "Paper",
            "ماذا تحتاج النباتات لتنمو؟", "الماء", "الرمل", "الورق"),
        new("Which one is hot?", "The sun", "Ice", "Snow",
            "أي مما يلي ساخن؟", "الشمس", "الثلج", "الجليد"),
        new("How many legs does a bird have?", "Two", "Four", "Six",
            "كم رِجلًا للطائر؟", "اثنتان", "أربع", "ست"),
        new("What do we drink when we are thirsty?", "Water", "Sand", "Stones",
            "ماذا نشرب عندما نعطش؟", "الماء", "الرمل", "الحجارة"),
        new("Which animal gives us milk?", "A cow", "A cat", "A fish",
            "أي حيوان يعطينا اللبن؟", "البقرة", "القطة", "السمكة"),
        new("What do we use to smell?", "Our nose", "Our hand", "Our foot",
            "بماذا نشم؟", "بالأنف", "باليد", "بالقدم"),
        new("When do we see the moon?", "At night", "At noon", "In the morning",
            "متى نرى القمر؟", "في الليل", "في الظهيرة", "في الصباح")
    ];

    private static readonly Fact[] SciencePrimary =
    [
        new("Which part of a plant takes in water from the soil?", "The roots", "The leaves", "The flower",
            "أي جزء من النبات يمتص الماء من التربة؟", "الجذور", "الأوراق", "الزهرة"),
        new("Which gas do we take in when we breathe?", "Oxygen", "Carbon dioxide", "Hydrogen",
            "ما الغاز الذي نستنشقه عند التنفس؟", "الأكسجين", "ثاني أكسيد الكربون", "الهيدروجين"),
        new("Water turns into vapour when it is:", "Heated", "Frozen", "Filtered",
            "يتحول الماء إلى بخار عندما:", "يُسخَّن", "يتجمد", "يُرشَّح"),
        new("A magnet attracts objects made of:", "Iron", "Wood", "Plastic",
            "ينجذب المغناطيس إلى الأجسام المصنوعة من:", "الحديد", "الخشب", "البلاستيك"),
        new("Which organ pumps blood around the body?", "The heart", "The lungs", "The stomach",
            "أي عضو يضخ الدم في الجسم؟", "القلب", "الرئتان", "المعدة"),
        new("The Earth moves around the:", "Sun", "Moon", "Mars",
            "تدور الأرض حول:", "الشمس", "القمر", "المريخ"),
        new("Which state of matter has a fixed shape?", "Solid", "Liquid", "Gas",
            "أي حالات المادة لها شكل ثابت؟", "الصلبة", "السائلة", "الغازية"),
        new("What do plants make using sunlight?", "Their food", "Rocks", "Rain",
            "ماذا تصنع النباتات مستخدمة ضوء الشمس؟", "غذاءها", "الصخور", "المطر"),
        new("Which of these animals is a mammal?", "A dolphin", "A crocodile", "An eagle",
            "أي الحيوانات التالية من الثدييات؟", "الدلفين", "التمساح", "النسر"),
        new("Sound travels fastest through:", "Steel", "Air", "A vacuum",
            "ينتقل الصوت أسرع خلال:", "الحديد", "الهواء", "الفراغ"),
        new("At what temperature does water freeze, in °C?", "0", "100", "50",
            "عند أي درجة حرارة يتجمد الماء بالسيليزية؟", "0", "100", "50"),
        new("Which sense organ detects light?", "The eye", "The ear", "The skin",
            "أي عضو حسي يستقبل الضوء؟", "العين", "الأذن", "الجلد")
    ];

    private static readonly Fact[] SciencePreparatory =
    [
        new("What is the basic unit of life?", "The cell", "The atom", "The molecule",
            "ما الوحدة الأساسية للحياة؟", "الخلية", "الذرة", "الجزيء"),
        new("What is the chemical formula of water?", "H₂O", "CO₂", "O₂",
            "ما الصيغة الكيميائية للماء؟", "H₂O", "CO₂", "O₂"),
        new("Which force pulls objects towards the Earth?", "Gravity", "Friction", "Magnetism",
            "ما القوة التي تجذب الأجسام نحو الأرض؟", "الجاذبية", "الاحتكاك", "المغناطيسية"),
        new("Which organ filters waste from the blood?", "The kidney", "The liver", "The lung",
            "أي عضو يرشّح الفضلات من الدم؟", "الكلية", "الكبد", "الرئة"),
        new("What is the unit of electric current?", "The ampere", "The volt", "The ohm",
            "ما وحدة شدة التيار الكهربي؟", "الأمبير", "الفولت", "الأوم"),
        new("Photosynthesis releases which gas?", "Oxygen", "Nitrogen", "Methane",
            "تُطلق عملية البناء الضوئي أي غاز؟", "الأكسجين", "النيتروجين", "الميثان"),
        new("Which particles are found in the nucleus of an atom?", "Protons and neutrons", "Electrons only", "Electrons and protons",
            "ما الجسيمات الموجودة في نواة الذرة؟", "البروتونات والنيوترونات", "الإلكترونات فقط", "الإلكترونات والبروتونات"),
        new("Speed is distance divided by:", "Time", "Mass", "Force",
            "السرعة هي المسافة مقسومة على:", "الزمن", "الكتلة", "القوة"),
        new("Which blood cells defend the body against infection?", "White blood cells", "Red blood cells", "Platelets",
            "أي خلايا الدم تدافع عن الجسم ضد العدوى؟", "كرات الدم البيضاء", "كرات الدم الحمراء", "الصفائح الدموية"),
        new("What is the pH of a neutral solution?", "7", "0", "14",
            "ما الرقم الهيدروجيني لمحلول متعادل؟", "7", "0", "14"),
        new("Energy stored in food is which kind of energy?", "Chemical", "Nuclear", "Sound",
            "الطاقة المخزنة في الغذاء هي طاقة:", "كيميائية", "نووية", "صوتية"),
        new("Which planet is closest to the Sun?", "Mercury", "Venus", "Earth",
            "أي الكواكب أقرب إلى الشمس؟", "عطارد", "الزهرة", "الأرض")
    ];

    private static readonly Fact[] ScienceSecondary =
    [
        new("Newton's second law states that force equals:", "Mass × acceleration", "Mass × velocity", "Mass ÷ acceleration",
            "ينص قانون نيوتن الثاني على أن القوة تساوي:", "الكتلة × العجلة", "الكتلة × السرعة", "الكتلة ÷ العجلة"),
        new("How many chromosomes are in a human body cell?", "46", "23", "92",
            "كم عدد الكروموسومات في الخلية الجسدية للإنسان؟", "46", "23", "92"),
        new("Which molecule stores genetic information?", "DNA", "ATP", "Protein",
            "أي جزيء يخزّن المعلومات الوراثية؟", "الحمض النووي DNA", "مركب ATP", "البروتين"),
        new("What is the SI unit of force?", "The newton", "The joule", "The watt",
            "ما وحدة القوة في النظام الدولي؟", "النيوتن", "الجول", "الواط"),
        new("An acid reacting with a base produces:", "A salt and water", "A gas only", "An acid only",
            "ينتج عن تفاعل حمض مع قاعدة:", "ملح وماء", "غاز فقط", "حمض فقط"),
        new("Which organelle produces most of the cell's ATP?", "The mitochondrion", "The ribosome", "The nucleus",
            "أي عضية تنتج معظم مركب ATP في الخلية؟", "الميتوكوندريا", "الريبوسوم", "النواة"),
        new("Ohm's law states that voltage equals:", "Current × resistance", "Current ÷ resistance", "Resistance ÷ current",
            "ينص قانون أوم على أن فرق الجهد يساوي:", "شدة التيار × المقاومة", "شدة التيار ÷ المقاومة", "المقاومة ÷ شدة التيار"),
        new("The atomic number of an element equals its number of:", "Protons", "Neutrons", "Neutrons and protons",
            "العدد الذري للعنصر يساوي عدد:", "البروتونات", "النيوترونات", "النيوترونات والبروتونات"),
        new("Which of these is a noble gas?", "Neon", "Oxygen", "Chlorine",
            "أي مما يلي غاز خامل؟", "النيون", "الأكسجين", "الكلور"),
        new("What is the speed of light in a vacuum, in m/s?", "3 × 10⁸", "3 × 10⁶", "3 × 10¹⁰",
            "ما سرعة الضوء في الفراغ بوحدة م/ث؟", "3 × 10⁸", "3 × 10⁶", "3 × 10¹⁰"),
        new("Which type of cell division produces gametes?", "Meiosis", "Mitosis", "Osmosis",
            "أي نوع من الانقسام الخلوي ينتج الأمشاج؟", "الانقسام الميوزي", "الانقسام الميتوزي", "الانتشار الأسموزي"),
        new("What is the SI unit of energy?", "The joule", "The newton", "The pascal",
            "ما وحدة الطاقة في النظام الدولي؟", "الجول", "النيوتن", "الباسكال")
    ];

    // ── English ───────────────────────────────────────────────────────────────

    private static readonly Fact[] EnglishKindergarten =
    [
        new("Which letter comes after A?", "B", "C", "D",
            "ما الحرف الذي يأتي بعد A؟", "B", "C", "D"),
        new("What is the first letter of the word \"cat\"?", "C", "A", "T",
            "ما الحرف الأول في كلمة \"cat\"؟", "C", "A", "T"),
        new("Which word names a colour?", "Red", "Jump", "Book",
            "أي كلمة تدل على لون؟", "Red", "Jump", "Book"),
        new("How do we greet someone in the morning?", "Good morning", "Good night", "Goodbye",
            "كيف نحيي شخصًا في الصباح؟", "Good morning", "Good night", "Goodbye"),
        new("Which word is a number?", "Three", "Chair", "Green",
            "أي كلمة تدل على عدد؟", "Three", "Chair", "Green"),
        new("What is the opposite of \"big\"?", "Small", "Tall", "Fast",
            "ما عكس كلمة \"big\"؟", "Small", "Tall", "Fast"),
        new("Which of these is an animal?", "Dog", "Table", "Shoe",
            "أي مما يلي حيوان؟", "Dog", "Table", "Shoe"),
        new("How many letters are in the word \"sun\"?", "Three", "Two", "Four",
            "كم عدد حروف كلمة \"sun\"؟", "ثلاثة", "اثنان", "أربعة"),
        new("Which word begins with the letter M?", "Moon", "Sun", "Star",
            "أي كلمة تبدأ بالحرف M؟", "Moon", "Sun", "Star"),
        new("What do we say when someone helps us?", "Thank you", "Goodbye", "Hello",
            "ماذا نقول لمن يساعدنا؟", "Thank you", "Goodbye", "Hello"),
        new("Which of these is a vowel?", "A", "B", "C",
            "أي الحروف التالية حرف علة؟", "A", "B", "C"),
        new("Which word means \"father\"?", "Dad", "Dog", "Door",
            "أي كلمة تعني \"الأب\"؟", "Dad", "Dog", "Door")
    ];

    private static readonly Fact[] EnglishPrimary =
    [
        new("What is the plural of \"child\"?", "Children", "Childs", "Childes",
            "ما جمع كلمة \"child\"؟", "Children", "Childs", "Childes"),
        new("What is the opposite of \"hot\"?", "Cold", "Warm", "Dry",
            "ما عكس كلمة \"hot\"؟", "Cold", "Warm", "Dry"),
        new("Complete: \"She ___ to school every day.\"", "goes", "go", "going",
            "أكمل: \"She ___ to school every day.\"", "goes", "go", "going"),
        new("Which word is a verb?", "Write", "Table", "Blue",
            "أي الكلمات التالية فعل؟", "Write", "Table", "Blue"),
        new("What is the past tense of \"eat\"?", "Ate", "Eated", "Eaten",
            "ما صيغة الماضي من الفعل \"eat\"؟", "Ate", "Eated", "Eaten"),
        new("Complete: \"___ apple a day keeps the doctor away.\"", "An", "A", "Some",
            "أكمل: \"___ apple a day keeps the doctor away.\"", "An", "A", "Some"),
        new("What is the plural of \"box\"?", "Boxes", "Boxs", "Boxen",
            "ما جمع كلمة \"box\"؟", "Boxes", "Boxs", "Boxen"),
        new("Which word is a noun?", "Teacher", "Quickly", "Happily",
            "أي الكلمات التالية اسم؟", "Teacher", "Quickly", "Happily"),
        new("What is the opposite of \"always\"?", "Never", "Often", "Sometimes",
            "ما عكس كلمة \"always\"؟", "Never", "Often", "Sometimes"),
        new("Complete: \"I ___ a book yesterday.\"", "read", "reads", "reading",
            "أكمل: \"I ___ a book yesterday.\"", "read", "reads", "reading"),
        new("Which word is spelled correctly?", "Beautiful", "Beutiful", "Beautifull",
            "أي الكلمات التالية مكتوبة إملائيًا بشكل صحيح؟", "Beautiful", "Beutiful", "Beautifull"),
        new("Complete: \"We go to school ___ Sunday.\"", "on", "in", "at",
            "أكمل: \"We go to school ___ Sunday.\"", "on", "in", "at")
    ];

    private static readonly Fact[] EnglishPreparatory =
    [
        new("Complete: \"He has ___ home already.\"", "gone", "went", "going",
            "أكمل: \"He has ___ home already.\"", "gone", "went", "going"),
        new("Which word is a comparative adjective?", "Faster", "Fast", "Fastest",
            "أي الكلمات التالية اسم تفضيل مقارن؟", "Faster", "Fast", "Fastest"),
        new("Put into the passive: \"They build houses.\"", "Houses are built.", "Houses build.", "Houses were building.",
            "حوّل إلى المبني للمجهول: \"They build houses.\"", "Houses are built.", "Houses build.", "Houses were building."),
        new("Complete: \"If it rains, we ___ stay at home.\"", "will", "would", "had",
            "أكمل: \"If it rains, we ___ stay at home.\"", "will", "would", "had"),
        new("Which word means the same as \"difficult\"?", "Hard", "Easy", "Simple",
            "أي كلمة تحمل معنى \"difficult\"؟", "Hard", "Easy", "Simple"),
        new("Which word is an adverb?", "Quickly", "Quick", "Quickness",
            "أي الكلمات التالية ظرف؟", "Quickly", "Quick", "Quickness"),
        new("Report this: He said, \"I am tired.\" → He said he ___ tired.", "was", "is", "were",
            "حوّل إلى الكلام المنقول: He said, \"I am tired.\" → He said he ___ tired.", "was", "is", "were"),
        new("What is the opposite of \"increase\"?", "Decrease", "Expand", "Enlarge",
            "ما عكس كلمة \"increase\"؟", "Decrease", "Expand", "Enlarge"),
        new("Complete: \"There ___ many students in the class.\"", "are", "is", "was",
            "أكمل: \"There ___ many students in the class.\"", "are", "is", "was"),
        new("What is the superlative of \"good\"?", "Best", "Gooder", "Goodest",
            "ما صيغة التفضيل العليا من \"good\"؟", "Best", "Gooder", "Goodest"),
        new("Complete: \"Neither of the boys ___ present.\"", "is", "are", "were",
            "أكمل: \"Neither of the boys ___ present.\"", "is", "are", "were"),
        new("Which word is a relative pronoun?", "Which", "Very", "Quickly",
            "أي الكلمات التالية اسم موصول؟", "Which", "Very", "Quickly")
    ];

    private static readonly Fact[] EnglishSecondary =
    [
        new("What does \"meticulous\" mean?", "Very careful about detail", "Very fast", "Very loud",
            "ما معنى كلمة \"meticulous\"؟", "شديد الدقة والعناية بالتفاصيل", "سريع جدًا", "مرتفع الصوت"),
        new("Complete: \"Had I known, I ___ have come earlier.\"", "would", "will", "did",
            "أكمل: \"Had I known, I ___ have come earlier.\"", "would", "will", "did"),
        new("Which word is a gerund?", "Swimming", "Swim", "Swam",
            "أي الكلمات التالية اسم فعل (gerund)؟", "Swimming", "Swim", "Swam"),
        new("What does \"inevitable\" mean?", "Unavoidable", "Optional", "Unlikely",
            "ما معنى كلمة \"inevitable\"؟", "لا مفر منه", "اختياري", "غير مُرجَّح"),
        new("Which sentence is correct?", "Hardly had he arrived when it rained.", "Hardly he had arrived when it rained.", "Hardly he arrived when it rained.",
            "أي الجمل التالية صحيحة؟", "Hardly had he arrived when it rained.", "Hardly he had arrived when it rained.", "Hardly he arrived when it rained."),
        new("What is the opposite of \"scarce\"?", "Abundant", "Rare", "Limited",
            "ما عكس كلمة \"scarce\"؟", "Abundant", "Rare", "Limited"),
        new("Complete: \"The report ___ by the manager yesterday.\"", "was written", "wrote", "has written",
            "أكمل: \"The report ___ by the manager yesterday.\"", "was written", "wrote", "has written"),
        new("What does \"concise\" mean?", "Brief and clear", "Long and detailed", "Loud and forceful",
            "ما معنى كلمة \"concise\"؟", "موجز وواضح", "طويل ومفصّل", "قوي وصاخب"),
        new("Which word is a subordinating conjunction?", "Although", "And", "But",
            "أي الكلمات التالية أداة ربط للجملة التابعة؟", "Although", "And", "But"),
        new("Complete: \"I wish I ___ more time.\"", "had", "have", "will have",
            "أكمل: \"I wish I ___ more time.\"", "had", "have", "will have"),
        new("What does \"ambiguous\" mean?", "Open to more than one meaning", "Perfectly clear", "Extremely short",
            "ما معنى كلمة \"ambiguous\"؟", "يحتمل أكثر من معنى", "واضح تمامًا", "قصير جدًا"),
        new("Which sentence is correct?", "She suggested that he go.", "She suggested that he goes.", "She suggested him to go.",
            "أي الجمل التالية صحيحة؟", "She suggested that he go.", "She suggested that he goes.", "She suggested him to go.")
    ];

    // ── Arabic ────────────────────────────────────────────────────────────────

    private static readonly Fact[] ArabicKindergarten =
    [
        new("What is the first letter of the word \"أسد\"?", "أ", "ب", "ت",
            "ما الحرف الأول في كلمة \"أسد\"؟", "أ", "ب", "ت"),
        new("Which letter comes after \"ب\"?", "ت", "أ", "ث",
            "ما الحرف الذي يأتي بعد \"ب\"؟", "ت", "أ", "ث"),
        new("Which of these words names an animal?", "قط", "باب", "قلم",
            "أي الكلمات التالية اسم حيوان؟", "قط", "باب", "قلم"),
        new("What is the opposite of \"كبير\"?", "صغير", "طويل", "سريع",
            "ما ضد كلمة \"كبير\"؟", "صغير", "طويل", "سريع"),
        new("How many letters are in the word \"قمر\"?", "Three", "Two", "Four",
            "كم حرفًا في كلمة \"قمر\"؟", "ثلاثة", "اثنان", "أربعة"),
        new("Which word begins with the letter \"م\"?", "مدرسة", "كتاب", "شمس",
            "أي كلمة تبدأ بحرف \"م\"؟", "مدرسة", "كتاب", "شمس"),
        new("Which of these words names a colour?", "أحمر", "باب", "ولد",
            "أي الكلمات التالية تدل على لون؟", "أحمر", "باب", "ولد"),
        new("What is the plural of \"كتاب\"?", "كتب", "كاتب", "مكتب",
            "ما جمع كلمة \"كتاب\"؟", "كتب", "كاتب", "مكتب"),
        new("Which word means \"the sun\"?", "شمس", "قمر", "نجم",
            "أي كلمة تعني \"الشمس\"؟", "شمس", "قمر", "نجم"),
        new("What do we say when we meet someone?", "السلام عليكم", "مع السلامة", "تصبح على خير",
            "ماذا نقول عندما نقابل شخصًا؟", "السلام عليكم", "مع السلامة", "تصبح على خير"),
        new("Which of these is an Arabic letter?", "ص", "٥", "+",
            "أي مما يلي حرف عربي؟", "ص", "٥", "+"),
        new("What is the opposite of \"ليل\"?", "نهار", "مساء", "فجر",
            "ما ضد كلمة \"ليل\"؟", "نهار", "مساء", "فجر")
    ];

    private static readonly Fact[] ArabicPrimary =
    [
        new("What is the plural of \"قلم\"?", "أقلام", "قالم", "مقلمة",
            "ما جمع كلمة \"قلم\"؟", "أقلام", "قالم", "مقلمة"),
        new("What is the opposite of \"سريع\"?", "بطيء", "نشيط", "قوي",
            "ما ضد كلمة \"سريع\"؟", "بطيء", "نشيط", "قوي"),
        new("What kind of word is \"يكتب\"?", "فعل", "اسم", "حرف",
            "ما نوع كلمة \"يكتب\"؟", "فعل", "اسم", "حرف"),
        new("Which of these is a preposition (حرف جر)?", "في", "كتب", "ولد",
            "أي مما يلي حرف جر؟", "في", "كتب", "ولد"),
        new("What is the singular of \"أشجار\"?", "شجرة", "أشجر", "شجيرة",
            "ما مفرد كلمة \"أشجار\"؟", "شجرة", "أشجر", "شجيرة"),
        new("What is the opposite of \"فرح\"?", "حزن", "ضحك", "لعب",
            "ما ضد كلمة \"فرح\"؟", "حزن", "ضحك", "لعب"),
        new("What kind of word is \"مدرسة\"?", "اسم", "فعل", "حرف",
            "ما نوع كلمة \"مدرسة\"؟", "اسم", "فعل", "حرف"),
        new("Complete: \"ذهب الولد ___ المدرسة.\"", "إلى", "على", "عن",
            "أكمل: \"ذهب الولد ___ المدرسة.\"", "إلى", "على", "عن"),
        new("What is the plural of \"بيت\"?", "بيوت", "بيتان", "أبيات",
            "ما جمع كلمة \"بيت\"؟", "بيوت", "بيتان", "أبيات"),
        new("Which of these names is feminine?", "فاطمة", "محمد", "علي",
            "أي الأسماء التالية مؤنث؟", "فاطمة", "محمد", "علي"),
        new("What is the opposite of \"قريب\"?", "بعيد", "جانب", "أمام",
            "ما ضد كلمة \"قريب\"؟", "بعيد", "جانب", "أمام"),
        new("Which mark ends a question in Arabic?", "؟", "!", ".",
            "ما العلامة التي تنتهي بها الجملة الاستفهامية؟", "؟", "!", ".")
    ];

    private static readonly Fact[] ArabicPreparatory =
    [
        new("In \"حضر الطالبُ\", what is the grammatical role of \"الطالبُ\"?", "فاعل مرفوع", "مفعول به منصوب", "مبتدأ",
            "ما إعراب \"الطالبُ\" في جملة \"حضر الطالبُ\"؟", "فاعل مرفوع", "مفعول به منصوب", "مبتدأ"),
        new("What marks the nominative case of a sound masculine plural?", "الواو", "الألف", "الضمة",
            "ما علامة رفع جمع المذكر السالم؟", "الواو", "الألف", "الضمة"),
        new("What kind of particle is \"لن\" in \"لن أذهبَ\"?", "حرف نصب", "حرف جزم", "حرف جر",
            "ما نوع \"لن\" في جملة \"لن أذهبَ\"؟", "حرف نصب", "حرف جزم", "حرف جر"),
        new("What is the sound masculine plural of \"معلّم\"?", "معلمون", "معالم", "معلمات",
            "ما جمع المذكر السالم لكلمة \"معلّم\"؟", "معلمون", "معالم", "معلمات"),
        new("In \"قرأ محمدٌ الكتابَ\", which word is the object?", "الكتابَ", "محمدٌ", "قرأ",
            "ما المفعول به في جملة \"قرأ محمدٌ الكتابَ\"؟", "الكتابَ", "محمدٌ", "قرأ"),
        new("What type of sentence is \"السماءُ صافيةٌ\"?", "جملة اسمية", "جملة فعلية", "جملة شرطية",
            "ما نوع جملة \"السماءُ صافيةٌ\"؟", "جملة اسمية", "جملة فعلية", "جملة شرطية"),
        new("What marks the accusative case of the dual?", "الياء", "الألف", "الضمة",
            "ما علامة نصب المثنى؟", "الياء", "الألف", "الضمة"),
        new("What is the opposite of \"التفاؤل\"?", "التشاؤم", "الأمل", "السرور",
            "ما ضد كلمة \"التفاؤل\"؟", "التشاؤم", "الأمل", "السرور"),
        new("In \"كتب الطالبُ الدرسَ\", the verb \"كتب\" is:", "فعل ماضٍ", "فعل مضارع", "فعل أمر",
            "الفعل \"كتب\" في جملة \"كتب الطالبُ الدرسَ\" هو:", "فعل ماضٍ", "فعل مضارع", "فعل أمر"),
        new("In \"نجح المجتهدون\", which word is the subject?", "المجتهدون", "نجح", "لا فاعل في الجملة",
            "ما الفاعل في جملة \"نجح المجتهدون\"؟", "المجتهدون", "نجح", "لا فاعل في الجملة"),
        new("Which of these belongs to the Five Nouns (الأسماء الخمسة)?", "أبو", "كتاب", "قلم",
            "أي مما يلي من الأسماء الخمسة؟", "أبو", "كتاب", "قلم"),
        new("What kind of \"كم\" appears in \"كم كتابًا قرأت؟\"", "استفهامية", "خبرية", "شرطية",
            "ما نوع \"كم\" في جملة \"كم كتابًا قرأت؟\"", "استفهامية", "خبرية", "شرطية")
    ];

    private static readonly Fact[] ArabicSecondary =
    [
        new("Which rhetorical device appears in \"الليل والنهار\"?", "طباق", "جناس", "سجع",
            "ما المحسن البديعي في \"الليل والنهار\"؟", "طباق", "جناس", "سجع"),
        new("\"العلم نور\" is an example of:", "تشبيه بليغ", "استعارة مكنية", "كناية",
            "\"العلم نور\" أسلوب:", "تشبيه بليغ", "استعارة مكنية", "كناية"),
        new("What kind of particle is \"إن\" in \"إن تجتهد تنجح\"?", "حرف شرط جازم", "حرف توكيد", "حرف نصب",
            "ما نوع \"إن\" في \"إن تجتهد تنجح\"؟", "حرف شرط جازم", "حرف توكيد", "حرف نصب"),
        new("Which metre has the foot pattern \"فعولن مفاعيلن\"?", "البحر الطويل", "البحر الكامل", "بحر الرجز",
            "ما البحر الشعري الذي تفعيلته \"فعولن مفاعيلن\"؟", "البحر الطويل", "البحر الكامل", "بحر الرجز"),
        new("What kind of metaphor is \"ابتسم الصباح\"?", "استعارة مكنية", "استعارة تصريحية", "استعارة تمثيلية",
            "ما نوع الاستعارة في \"ابتسم الصباح\"؟", "استعارة مكنية", "استعارة تصريحية", "استعارة تمثيلية"),
        new("What marks the genitive case of a diptote (الممنوع من الصرف)?", "الفتحة", "الكسرة", "السكون",
            "ما علامة جر الممنوع من الصرف؟", "الفتحة", "الكسرة", "السكون"),
        new("What kind of \"ما\" appears in \"ما أجملَ السماءَ!\"?", "تعجبية", "نافية", "استفهامية",
            "ما نوع \"ما\" في \"ما أجملَ السماءَ!\"؟", "تعجبية", "نافية", "استفهامية"),
        new("الجناس is a similarity in:", "اللفظ مع اختلاف المعنى", "المعنى مع اختلاف اللفظ", "الوزن فقط",
            "الجناس هو التشابه في:", "اللفظ مع اختلاف المعنى", "المعنى مع اختلاف اللفظ", "الوزن فقط"),
        new("In \"كن طالبًا مجتهدًا\", what is \"طالبًا\"?", "خبر كان منصوب", "اسم كان", "مفعول به",
            "ما إعراب \"طالبًا\" في \"كن طالبًا مجتهدًا\"؟", "خبر كان منصوب", "اسم كان", "مفعول به"),
        new("Which poet belongs to the Romantic school (مدرسة أبولو)?", "علي محمود طه", "أحمد شوقي", "حافظ إبراهيم",
            "أي الشعراء التالين ينتمي إلى المدرسة الرومانسية؟", "علي محمود طه", "أحمد شوقي", "حافظ إبراهيم"),
        new("\"فلان كثير الرماد\" is a كناية about:", "الكرم", "البخل", "الشجاعة",
            "\"فلان كثير الرماد\" كناية عن:", "الكرم", "البخل", "الشجاعة"),
        new("What kind of particle is \"لو\" in \"لو اجتهدت لنجحت\"?", "حرف امتناع لامتناع", "حرف جزم", "حرف نصب",
            "ما نوع \"لو\" في \"لو اجتهدت لنجحت\"؟", "حرف امتناع لامتناع", "حرف جزم", "حرف نصب")
    ];

    // ── Social studies ────────────────────────────────────────────────────────

    private static readonly Fact[] SocialStudies =
    [
        new("What is the capital of Egypt?", "Cairo", "Alexandria", "Aswan",
            "ما عاصمة مصر؟", "القاهرة", "الإسكندرية", "أسوان"),
        new("Which river runs through Egypt?", "The Nile", "The Amazon", "The Danube",
            "ما النهر الذي يمر بمصر؟", "النيل", "الأمازون", "الدانوب"),
        new("Most of Egypt lies in which continent?", "Africa", "Europe", "Australia",
            "تقع معظم أراضي مصر في قارة:", "أفريقيا", "أوروبا", "أستراليا"),
        new("Where is the High Dam?", "Aswan", "Cairo", "Luxor",
            "أين يقع السد العالي؟", "أسوان", "القاهرة", "الأقصر"),
        new("Which sea lies to the north of Egypt?", "The Mediterranean", "The Red Sea", "The Black Sea",
            "أي بحر يقع شمال مصر؟", "البحر المتوسط", "البحر الأحمر", "البحر الأسود"),
        new("What was the writing of the ancient Egyptians called?", "Hieroglyphics", "Cuneiform", "Latin script",
            "بماذا سُميت كتابة المصريين القدماء؟", "الهيروغليفية", "المسمارية", "اللاتينية"),
        new("The Suez Canal connects the Mediterranean to:", "The Red Sea", "The Atlantic Ocean", "The Caspian Sea",
            "تربط قناة السويس البحر المتوسط بـ:", "البحر الأحمر", "المحيط الأطلسي", "بحر قزوين"),
        new("Which is the largest continent by area?", "Asia", "Africa", "Europe",
            "ما أكبر القارات مساحة؟", "آسيا", "أفريقيا", "أوروبا"),
        new("What is the climate zone at the equator called?", "Tropical", "Polar", "Temperate",
            "ما اسم المنطقة المناخية عند خط الاستواء؟", "الاستوائية", "القطبية", "المعتدلة"),
        new("In which year did the 23 July Revolution take place?", "1952", "1919", "1973",
            "في أي عام قامت ثورة 23 يوليو؟", "1952", "1919", "1973"),
        new("Which governorate is known as the Bride of the Mediterranean?", "Alexandria", "Port Said", "Suez",
            "أي محافظة تُلقب بعروس البحر المتوسط؟", "الإسكندرية", "بورسعيد", "السويس"),
        new("What does the scale on a map show?", "The ratio between map distance and real distance", "The colours used", "The date it was drawn",
            "ماذا يوضح مقياس الرسم على الخريطة؟", "النسبة بين المسافة على الخريطة والمسافة الحقيقية", "الألوان المستخدمة", "تاريخ رسمها")
    ];
}
