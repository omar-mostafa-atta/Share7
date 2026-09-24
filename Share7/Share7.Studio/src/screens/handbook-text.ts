// ===========================================================================
// The handbook
//
// How the Studio works, in the order a new member meets it. It is kept here as
// text rather than in the message dictionaries because it is prose, not
// labels: it is read once or twice and then only when something surprises
// somebody, and it should read like it was written by a person.
//
// It is deliberately short. Anything that needs explaining while you work is
// chalked beside the field it belongs to instead — that is the rule the whole
// Studio is built on, and a handbook that grows is a sign the boards are not
// explaining themselves.
// ===========================================================================

export interface Chapter {
  id: string
  title: string
  paragraphs: string[]
  points?: string[]
}

const en: Chapter[] = [
  {
    id: 'what',
    title: 'What the Studio is',
    paragraphs: [
      'Share7 is a game students play. The questions they answer are written here, by you, and nowhere else.',
      'Nothing you write reaches a student until two things have happened: somebody who did not write it has approved it, and a Lead has put it in a release. Until then the game carries on serving exactly what it served this morning.',
      'Everything is written down — who wrote what, who approved it, and when it went out. That record cannot be edited, by anyone, including whoever runs the team.',
    ],
  },
  {
    id: 'role',
    title: 'What you may do',
    paragraphs: ['Everybody has one role, and it is shown on your account board.'],
    points: [
      'An **Author** writes drafts in their part of the curriculum and submits them.',
      'A **Reviewer** does that, and approves or sends back other people’s drafts.',
      'A **Lead** does that, and bundles approved work into releases, sends them out, and puts them back if something is wrong.',
      'Your **scope** is the part of the curriculum and the languages you may change. It never limits what you may read: everybody can see everything and comment on anything.',
    ],
  },
  {
    id: 'writing',
    title: 'Writing a lesson',
    paragraphs: [
      'Open a lesson from the curriculum and press Start writing. The whole lesson appears on one board, written out the way it would be read aloud: a number, the question, its three answers indented beneath, the right one underlined, and the same question in each language under the same number.',
      'One draft per lesson, shared. If a colleague is already writing it you join them rather than starting a second copy — their name appears on the ledge while you both have it open. It saves as you type.',
      'A question you do not touch keeps its identity. That matters more than it sounds: a child’s history is attached to the question, so fixing an Arabic typo must not throw away what English-speaking children did with that question last term. The margin says **Unchanged** beside every line that is keeping its place.',
    ],
  },
  {
    id: 'checks',
    title: 'The checks',
    paragraphs: [
      'The ledge always says how many things are left to fix, and every one of them is chalked in the margin beside the exact line it belongs to. You cannot submit a lesson that still breaks a rule.',
      'The rules are the ones the game needs: every question filled in, in every language the game must have; exactly three answers, all different, with one marked right; and at least one second-chance question in a lesson that has questions.',
    ],
  },
  {
    id: 'review',
    title: 'Review',
    paragraphs: [
      'Submit for review puts the lesson in one queue that everybody shares, oldest first. Whoever picks it up must not have written any part of it — that is checked, not trusted.',
      'A reviewer either approves it or sends it back with a note saying what to change. Any edit after an approval cancels that approval, because what was read is no longer what is there.',
      'Comments can be pinned to one question, and a pinned comment also appears in the margin beside it.',
    ],
  },
  {
    id: 'release',
    title: 'Releases',
    paragraphs: [
      'A Lead gathers approved work into a release. Before it goes out, the board says exactly what would stop it and how many students are part way through anything it touches.',
      'Sending a release is all-or-nothing. If any part of it is refused, the whole thing is refused and nothing changed — you fix what it names and send it again.',
      'A release can be scheduled for a time you choose, such as the start of term.',
    ],
  },
  {
    id: 'rollback',
    title: 'Putting a release back',
    paragraphs: [
      'Any release that went out can be put back. That is itself a release: it restores what each change replaced, and the version number students see goes forward, never backwards, so no device can mistake old content for new.',
      'It is refused when something the release changed has been changed again since — that later work would be undone along with it, so it has to be put back first.',
    ],
  },
  {
    id: 'excel',
    title: 'Excel',
    paragraphs: [
      'A spreadsheet is still a good way to write a lot of questions. Try a sheet first reads it and reports every problem on its own row without saving anything; Bring in a sheet puts it into the open draft.',
      'A row that lands on a question already in the lesson keeps that question, so re-importing a sheet with one row fixed leaves every other question exactly as it was.',
      'You can take a lesson back out as a sheet, either what students have now or the open draft, and download a blank one to start from.',
    ],
  },
  {
    id: 'practice',
    title: 'Practising',
    paragraphs: [
      'A practice lesson behaves like any other — it is written, checked and reviewed the same way — except it is never released and is not attached to anything real.',
      'It is the place to learn the Studio, and the place to try something before doing it to a real lesson. Start one from Home.',
    ],
  },
  {
    id: 'account',
    title: 'Your account and 2-step',
    paragraphs: [
      'Your account was made for you and handed over as a one-time link. Nobody can send you a password by email, because the Studio sends no email at all.',
      '2-step asks for a six-digit code from an app on your phone after your password. Turning it on gives you ten backup codes — keep them somewhere safe, because they are the way back in if you lose the phone.',
      'Your account board shows every place you are signed in, and you can sign any of them out.',
    ],
  },
]

const ar: Chapter[] = [
  {
    id: 'what',
    title: 'ما هو الاستوديو',
    paragraphs: [
      'شير٧ لعبة يلعبها الطلاب. والأسئلة التي يجيبون عنها تُكتب هنا، بأيديكم، ولا تُكتب في أي مكان آخر.',
      'لا يصل شيء مما تكتبه إلى طالب قبل أمرين: أن يوافق عليه شخص لم يكتبه، وأن يضعه قائد الفريق في إصدار. وحتى ذلك الحين تظل اللعبة تقدّم ما كانت تقدّمه هذا الصباح تمامًا.',
      'كل شيء مكتوب: من كتب ماذا، ومن وافق، ومتى خرج. وهذا السجل لا يمكن تعديله، لا منك ولا من المسؤول عن الفريق.',
    ],
  },
  {
    id: 'role',
    title: 'ما يمكنك عمله',
    paragraphs: ['لكل شخص دور واحد، وهو مكتوب في صفحة حسابك.'],
    points: [
      '**الكاتب** يكتب المسودات في الجزء المسموح له من المنهج ويرسلها.',
      '**المراجِع** يفعل ذلك، ويوافق على مسودات الآخرين أو يعيدها.',
      '**قائد الفريق** يفعل ذلك، ويجمع العمل المعتمد في إصدارات ويرسلها، ويُرجِعها إن حدث خطأ.',
      '**نطاقك** هو الجزء من المنهج واللغات التي يمكنك تغييرها. وهو لا يحدّ ما تقرؤه أبدًا: الجميع يرى كل شيء ويعلّق على أي شيء.',
    ],
  },
  {
    id: 'writing',
    title: 'كتابة درس',
    paragraphs: [
      'افتح درسًا من المنهج واضغط «ابدأ الكتابة». يظهر الدرس كله على سبورة واحدة، مكتوبًا كما يُقرأ بصوت عالٍ: رقم، ثم السؤال، ثم إجاباته الثلاث مزاحة تحته والصحيحة مسطَّرة، ثم السؤال نفسه بكل لغة تحت الرقم نفسه.',
      'مسودة واحدة لكل درس، يتشاركها الفريق. إن كان زميل يكتبها بالفعل فأنت تنضم إليه بدل أن تبدأ نسخة ثانية — ويظهر اسمه على الحافة ما دام الاثنان يفتحانها. والحفظ يتم أثناء الكتابة.',
      'السؤال الذي لا تلمسه يحتفظ بهويته. وهذا أهم مما يبدو: سجل كل طالب معلّق بالسؤال، فإصلاح خطأ إملائي بالعربية يجب ألّا يمحو ما فعله الطلاب مع السؤال الإنجليزي في الفصل الماضي. ويكتب الهامش «بلا تغيير» بجانب كل سطر يحتفظ بمكانه.',
    ],
  },
  {
    id: 'checks',
    title: 'الفحوص',
    paragraphs: [
      'تقول الحافة دائمًا كم بقي لإصلاحه، وكل واحد منها مكتوب في الهامش بجانب السطر الذي يخصه بالضبط. ولا يمكن إرسال درس ما زال يخالف قاعدة.',
      'والقواعد هي ما تحتاجه اللعبة: كل سؤال مكتوب بكل لغة مطلوبة، وثلاث إجابات مختلفة بالضبط وواحدة منها صحيحة، وسؤال فرصة ثانية واحد على الأقل في أي درس فيه أسئلة.',
    ],
  },
  {
    id: 'review',
    title: 'المراجعة',
    paragraphs: [
      '«أرسل للمراجعة» يضع الدرس في طابور واحد يشترك فيه الجميع، الأقدم أولًا. ومن يأخذه يجب ألّا يكون قد كتب أي جزء منه — وهذا يُتحقق منه، لا يُفترض.',
      'المراجع إما أن يوافق أو يعيد العمل مع ملاحظة تقول ما الذي يُغيَّر. وأي تعديل بعد الموافقة يلغيها، لأن ما قُرئ لم يعد هو الموجود.',
      'يمكن تثبيت التعليق على سؤال بعينه، والتعليق المثبَّت يظهر أيضًا في الهامش بجانبه.',
    ],
  },
  {
    id: 'release',
    title: 'الإصدارات',
    paragraphs: [
      'يجمع قائد الفريق العمل المعتمد في إصدار. وقبل خروجه تقول السبورة بالضبط ما الذي يمنعه، وكم طالبًا في منتصف شيء يمسّه.',
      'إرسال الإصدار كل شيء أو لا شيء. إن رُفض جزء منه رُفض كله ولم يتغيّر شيء — تُصلح ما ذُكر وترسله مرة أخرى.',
      'ويمكن جدولة الإصدار في وقت تختاره، مثل بداية الفصل الدراسي.',
    ],
  },
  {
    id: 'rollback',
    title: 'إرجاع إصدار',
    paragraphs: [
      'كل إصدار خرج يمكن إرجاعه. والإرجاع نفسه إصدار: يعيد ما استبدله كل تغيير، ورقم النسخة التي يراها الطالب يتقدّم ولا يعود للخلف، فلا يخلط أي جهاز بين القديم والجديد.',
      'ويُرفض الإرجاع إذا كان شيء غيّره الإصدار قد تغيّر مرة أخرى بعده — لأن ذلك العمل اللاحق سيُمحى معه، فيجب إرجاعه أولًا.',
    ],
  },
  {
    id: 'excel',
    title: 'إكسل',
    paragraphs: [
      'ما زال الملف طريقة جيدة لكتابة عدد كبير من الأسئلة. «جرّب الملف أولًا» يقرؤه ويعرض كل مشكلة على صفها دون حفظ أي شيء، و«أدخِل ملفًا» يضعه في المسودة المفتوحة.',
      'الصف الذي يقع على سؤال موجود في الدرس يحتفظ بذلك السؤال، فإعادة إدخال ملف بعد إصلاح صف واحد تترك بقية الأسئلة كما هي تمامًا.',
      'ويمكنك إخراج الدرس كملف، إما ما لدى الطلاب الآن أو المسودة المفتوحة، وتنزيل ملف فارغ للبدء منه.',
    ],
  },
  {
    id: 'practice',
    title: 'التدريب',
    paragraphs: [
      'الدرس التدريبي يتصرف كأي درس آخر — يُكتب ويُفحص ويُراجع بالطريقة نفسها — إلا أنه لا يُصدَر أبدًا وليس مرتبطًا بشيء حقيقي.',
      'وهو المكان المناسب لتعلّم الاستوديو، ولتجربة شيء قبل عمله في درس حقيقي. ابدأ واحدًا من الصفحة الرئيسية.',
    ],
  },
  {
    id: 'account',
    title: 'حسابك والخطوتان',
    paragraphs: [
      'حسابك أُنشئ لك وسُلّم إليك كرابط يعمل مرة واحدة. ولا أحد يستطيع إرسال كلمة مرور إليك بالبريد، لأن الاستوديو لا يرسل بريدًا على الإطلاق.',
      'الخطوتان تطلبان رمزًا من ستة أرقام من تطبيق على هاتفك بعد كلمة المرور. وتفعيلهما يعطيك عشرة رموز احتياطية — احتفظ بها في مكان آمن، فهي طريق عودتك إن فقدت الهاتف.',
      'وتعرض صفحة حسابك كل مكان سجّلت فيه الدخول، ويمكنك تسجيل الخروج من أي منها.',
    ],
  },
]

export const handbook = { en, ar }
