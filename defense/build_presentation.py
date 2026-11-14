from pathlib import Path
import sys,json,re,math,zipfile,textwrap
sys.stdout.reconfigure(encoding='utf-8')
from PIL import Image,ImageDraw
from reportlab.pdfgen import canvas
from reportlab.pdfbase import pdfmetrics
from reportlab.pdfbase.ttfonts import TTFont
from reportlab.lib.colors import HexColor
from pptx import Presentation
from pptx.util import Inches,Pt
from pptx.dml.color import RGBColor
from pptx.enum.shapes import MSO_SHAPE
from pptx.enum.text import MSO_ANCHOR,PP_ALIGN
from pptx.oxml.xmlchemy import OxmlElement
from docx import Document
from docx.shared import Pt as DocPt

ROOT=Path(__file__).resolve().parent
A=ROOT/'assets';F=A/'fonts'
W,H=1600,900
BG='#F5F8F5';WHITE='#FFFFFF';INK='#151A16';GREEN='#318B37';DARK='#215B35';SOFT='#E8F3E6';LINE='#DCE7DA';BLUE='#E8EFFF';GOLD='#F6EDCD'
fonts={'Regular':400,'Medium':500,'SemiBold':600,'Bold':700}
for name in fonts:pdfmetrics.registerFont(TTFont('Inter-'+name,str(F/f'Inter-{name}.ttf')))

SLIDES=[];current=None

def slide(title,notes):
 global current
 current={'title':title,'notes':notes,'elements':[]};SLIDES.append(current)
 rect(0,0,W,H,BG,0)

def rect(x,y,w,h,color=WHITE,r=28,stroke=None):
 current['elements'].append(dict(kind='rect',x=x,y=y,w=w,h=h,color=color,r=r,stroke=stroke))

def text(s,x,y,w,size=30,color=INK,font='Regular',leading=1.26,align='left'):
 lines=[]
 for para in s.split('\n'):
  line=''
  for word in para.split(' '):
   trial=(line+' '+word).strip()
   if line and pdfmetrics.stringWidth(trial,'Inter-'+font,size)>w:
    lines.append(line);line=word
   else:line=trial
  lines.append(line)
 height=len(lines)*size*leading+8
 current['elements'].append(dict(kind='text',text='\n'.join(lines),x=x,y=y,w=w,h=height,size=size,color=color,font=font,leading=leading,align=align))
 return height

def image(name,x,y,w,h=None):
 p=A/name
 iw,ih=Image.open(p).size
 if h is None:h=w*ih/iw
 else:
  scale=min(w/iw,h/ih);nw,nh=iw*scale,ih*scale;x+=(w-nw)/2;y+=(h-nh)/2;w,h=nw,nh
 current['elements'].append(dict(kind='image',file=str(p.relative_to(ROOT)),x=x,y=y,w=w,h=h))

def polyline(points,color=GREEN,width=3,closed=False):
 current['elements'].append(dict(kind='line',points=points,color=color,width=width,closed=closed))

def icon(name,x,y,size=48,color=GREEN):
 def p(points,closed=False):polyline([(x+a*size,y+b*size) for a,b in points],color,size*.055,closed)
 if name=='lock':
  p([(.28,.46),(.28,.28),(.36,.14),(.5,.1),(.64,.14),(.72,.28),(.72,.46)])
  p([(.17,.46),(.83,.46),(.83,.9),(.17,.9)],True);p([(.5,.62),(.5,.75)])
 elif name=='school':
  p([(.08,.38),(.5,.08),(.92,.38)]);p([(.17,.35),(.17,.91),(.83,.91),(.83,.35)]);p([(.42,.91),(.42,.68),(.58,.68),(.58,.91)])
  p([(.33,.45),(.33,.53)]);p([(.66,.45),(.66,.53)])
 elif name=='chart':
  p([(.12,.12),(.12,.88),(.9,.88)]);p([(.29,.72),(.29,.48)]);p([(.5,.72),(.5,.3)]);p([(.72,.72),(.72,.12)])
 elif name=='answers':
  p([(.1,.12),(.9,.12),(.9,.74),(.42,.74),(.22,.93),(.22,.74),(.1,.74)],True);p([(.28,.34),(.72,.34)]);p([(.28,.51),(.56,.51)])
 elif name=='api':
  p([(.31,.24),(.1,.5),(.31,.76)]);p([(.69,.24),(.9,.5),(.69,.76)]);p([(.58,.12),(.42,.88)])
 elif name=='quest':
  p([(.09,.19),(.35,.08),(.65,.22),(.91,.1),(.91,.82),(.65,.94),(.35,.8),(.09,.92)],True);p([(.35,.08),(.35,.8)]);p([(.65,.22),(.65,.94)]);p([(.47,.37),(.56,.48),(.47,.6)])
 elif name=='duel':
  p([(.13,.12),(.31,.16),(.79,.7),(.7,.79),(.16,.31),(.13,.12)]);p([(.11,.68),(.33,.91)]);p([(.22,.8),(.08,.94)])
  p([(.87,.12),(.69,.16),(.54,.34)]);p([(.87,.12),(.84,.31),(.66,.51)]);p([(.67,.91),(.89,.68)]);p([(.78,.8),(.92,.94)])
 elif name=='arrow':p([(.1,.5),(.9,.5)]);p([(.65,.25),(.9,.5),(.65,.75)])

def pill(label,x,y,w,color=SOFT,ink=DARK,size=22):
 rect(x,y,w,52,color,26);text(label,x+18,y+11,w-36,size,ink,'Medium',align='center')

def title(label):text(label,70,64,1450,56,INK,'SemiBold',leading=1.16)

SPEECH=[
('00:00–00:22','От первого слова к роли эксперта',
'Мы — команда «Дебют в проде». Наш проект — башкирская языковая школа, в которой человек учит язык, знакомится с культурой и постепенно начинает помогать с переводами. Мы хотим, чтобы занятие было коротким и понятным, а возвращаться в приложение хотелось каждый день.'),
('00:22–00:56','Путь ученика',
'Сначала пользователь проходит мини-игры: выбирает перевод слова, соединяет пары и собирает предложения. Задания дают опыт и помогают расти в звании. Босс-испытание открывается только с высшего звания «Оста»: для него нужны 250 решённых заданий и серия в 14 дней. Здесь уже три сложные фразы из настоящих текстов. Пользователь пробует себя в роли эксперта: сравнивает варианты, выбирает перевод или предлагает собственный. Так обучение подводит к осмысленному участию в рейтинговом переводе.'),
('00:56–01:24','Геймификация и образ',
'Мотивация не заканчивается на баллах. Есть серии занятий, уровни, звания, достижения и рейтинг. За учебную активность человек получает монеты, покупает ключи и открывает кейсы. Найденные фоны, рамки, питомцы и предметы попадают в инвентарь. Их можно применить к профилю, а тот же образ появляется в рейтинге. Питомцы и культурные предметы собраны в одном кейсе; повторная находка компенсируется монетами.'),
('01:24–01:46','Культура через коллекцию',
'Коллекция — ещё один способ учиться. Курай знакомит с музыкой, Акбузат — с башкирским эпосом, а Агидель — с географией и языком края. У каждой награды есть описание. Поэтому, собирая свой образ, человек одновременно узнаёт культуру Башкортостана. Сейчас в новой коллекции 18 иллюстрированных наград: фоны, питомцы и культурные предметы.'),
('01:46–02:35','Переводы, эксперты и интеграция',
'Для работы с данными отдельно реализованы серверное ядро, экспертный кабинет, LMS и Telegram-бот. Ядро хранит фрагменты с источниками, ответы и решения по переводам в PostgreSQL. Эксперт разбирает спорные варианты, может утвердить несколько допустимых переводов и видит статистику своей работы. Преподаватель выдаёт задания классу, анализирует ответы и получает отчёты. Переводы и результаты доступны через документированный API и выгрузки JSON и CSV. Есть инструкция по развёртыванию; повторные запросы защищены от дублирования. Игровой интерфейс и эти сервисы подготовлены как отдельные части проекта — их сквозное объединение остаётся следующим этапом.'),
('02:35–03:00','Дальнейшее развитие',
'Дальше хотим развивать три направления. Школьные лиги — чтобы классы и школы соревновались командами. Культурные квесты — чтобы изучение языка связывалось с историей, музыкой и местами Башкортостана. И дуэли — короткие языковые поединки один на один. Это даст новые причины практиковаться и общаться на башкирском языке.')
]

slide('Учить язык. Открывать культуру. Влиять на перевод.',SPEECH[0][2])
image('ornament.png',1270,10,280,280)
image('logo.png',70,58,66,66)
text('Башкирская\nязыковая школа',154,63,550,24,DARK,'SemiBold',leading=1.12)
text('Учить язык.\nОткрывать культуру.\nВлиять на перевод.',70,234,950,78,INK,'SemiBold',leading=1.17)
text('Короткие уроки, игра\nи настоящие башкирские тексты.',75,572,850,30,DARK,leading=1.38)
pill('Дебют в проде',75,755,270,SOFT,DARK,23)
rect(1072,54,444,790,'#E3EDE1',48)
rect(1088,68,412,764,WHITE,40)
image('home.png',1104,84,380,740)

slide('От первого слова — к роли эксперта',SPEECH[1][2])
title('От первого слова — к роли эксперта')
rect(70,202,420,625,WHITE,32)
text('01 · Учусь',100,234,355,29,GREEN,'SemiBold')
image('lesson.png',112,306,336,458)
rect(535,202,350,625,SOFT,32)
text('02 · Расту в звании',565,234,290,26,DARK,'SemiBold')
icon('lock',675,351,72)
text('Оста',565,452,290,54,DARK,'SemiBold',align='center')
text('250 заданий\n14 дней подряд',565,540,290,27,DARK,leading=1.45,align='center')
pill('Доступ к боссу',566,706,288,WHITE,DARK,22)
rect(930,202,600,625,WHITE,32)
text('03 · Босс-испытание',960,234,535,28,GREEN,'SemiBold')
text('Рейтинговый перевод · 3 фразы',960,281,540,22,DARK,'Medium')
image('boss.png',1040,329,378,475)
icon('arrow',498,495,28);icon('arrow',893,495,28)

slide('Награда становится частью твоего образа',SPEECH[2][2])
title('Награда становится частью твоего образа')
for label,x,w in [('Серия',70,136),('Опыт',220,129),('Звания',363,150),('Достижения',527,220)]:pill(label,x,151,w)
for x,w in [(70,455),(566,455),(1062,468)]:rect(x,244,w,577,WHITE,32)
text('Кейсы',100,280,390,32,INK,'SemiBold')
image('../'+str(Path('../client/src/assets/cases/pets-case.png')),110,344,375,260)
text('Ключи за монеты',100,670,390,28,DARK,'Medium')
text('Питомцы и культурные\nпредметы в одном кейсе',100,724,390,23,DARK)
text('Профиль',596,280,390,32,INK,'SemiBold')
image('profile.png',591,363,405,362)
text('Собери свою коллекцию',596,757,390,23,DARK)
text('Рейтинг',1092,280,390,32,INK,'SemiBold')
image('leaderboard.png',1087,359,418,366)
text('Твой образ видят другие',1092,757,410,23,DARK)

slide('Узнавать культуру, собирая коллекцию',SPEECH[3][2])
text('Узнавать культуру,\nсобирая коллекцию',70,66,1000,62,INK,'SemiBold',leading=1.17)
pill('18 коллекционных наград',1080,91,450,SOFT,DARK,24)
for x,img,label,body in [
 (70,'kurai.png','Курай','Музыка, песни\nи мастерство исполнителя'),
 (566,'akbuzat.png','Акбузат','Крылатый конь\nбашкирского эпоса'),
 (1062,'agidel.png','Агидель','Река Белая:\nгеография и язык края')]:
 rect(x,266,468,556,WHITE,32)
 image(img,x+38,296,392,300)
 text(label,x+34,628,400,37,INK,'SemiBold')
 text(body,x+34,697,400,25,DARK,leading=1.4)

slide('Ответы пользователей — данные для обучения',SPEECH[4][2])
text('Ответы пользователей —\nданные для обучения',70,64,1450,61,INK,'SemiBold',leading=1.17)
rect(70,270,505,553,DARK,32)
text('Рейтинговые\nпереводы',106,314,433,43,WHITE,'SemiBold',leading=1.17)
for label,y in [('Источник',496),('Варианты ответов',555),('Решение сообщества\nи эксперта',614)]:text(label,107,y,425,26,WHITE,'Regular',leading=1.3)
pill('Веб + Telegram',107,737,307,'#3F7750',WHITE,24)
for x,y,ico,label,body in [
 (615,270,'answers','Анализ ответов','Сравнение вариантов\nи разбор спорных случаев'),
 (1092,270,'chart','Статистика','Участие, точность\nи результаты класса'),
 (615,565,'school','LMS','Задания, классы\nи отчёты преподавателю'),
 (1092,565,'api','Документированный API','Переводы и результаты\nв JSON и CSV')]:
 rect(x,y,438,258,WHITE,30);icon(ico,x+28,y+24,43)
 text(label,x+28,y+89,385,27 if ico=='api' else 30,INK,'SemiBold')
 text(body,x+28,y+150,385,23,DARK,leading=1.4)

slide('Следующий уровень',SPEECH[5][2])
image('ornament.png',1250,15,280,280)
text('Следующий уровень',70,105,1400,72,INK,'SemiBold')
for x,ico,label,body,color in [
 (70,'school','Школьные лиги','Соревнования между\nклассами и школами',SOFT),
 (566,'quest','Культурные квесты','История, музыка и места\nБашкортостана в заданиях',GOLD),
 (1062,'duel','Дуэли','Языковые поединки\nодин на один',BLUE)]:
 rect(x,308,468,465,WHITE,32)
 rect(x+32,344,95,95,color,27);icon(ico,x+52,364,55)
 text(label,x+34,495,403,33,INK,'SemiBold')
 text(body,x+34,582,400,27,DARK,leading=1.45)

# Produce one scene model for a PDF and an editable PowerPoint.
def rgb(c):return RGBColor.from_string(c.lstrip('#'))
pr=Presentation();pr.slide_width=Inches(13.333333);pr.slide_height=Inches(7.5)
pr.core_properties.title='Башкирская языковая школа — финальная защита'
pr.core_properties.author='Дебют в проде'
pr.core_properties.subject='Учебный путь, культурная коллекция, экспертиза и интеграция'
S=0.6
pdf=canvas.Canvas(str(ROOT/'Final_defense.pdf'),pagesize=(W,H));pdf.setTitle(pr.core_properties.title);pdf.setAuthor('Дебют в проде')
for n,scene in enumerate(SLIDES,1):
 ps=pr.slides.add_slide(pr.slide_layouts[6]);ps.notes_slide.notes_text_frame.text=scene['notes']
 for e in scene['elements']:
  k=e['kind']
  if k=='rect':
   x,y,w,h=e['x'],e['y'],e['w'],e['h'];pdf.setFillColor(HexColor(e['color']))
   if e['stroke']:pdf.setStrokeColor(HexColor(e['stroke']))
   if e['r']:pdf.roundRect(x,H-y-h,w,h,e['r'],stroke=int(bool(e['stroke'])),fill=1)
   else:pdf.rect(x,H-y-h,w,h,stroke=0,fill=1)
   sh=ps.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE if e['r'] else MSO_SHAPE.RECTANGLE,Pt(x*S),Pt(y*S),Pt(w*S),Pt(h*S))
   sh.fill.solid();sh.fill.fore_color.rgb=rgb(e['color']);sh.line.fill.background()
   if e['r']:
    try:sh.adjustments[0]=min(.5,e['r']/min(w,h))
    except Exception:pass
  elif k=='text':
   f='Inter-'+e['font'];size=e['size'];pdf.setFont(f,size);pdf.setFillColor(HexColor(e['color']))
   for i,line in enumerate(e['text'].split('\n')):
    tx=e['x'];sw=pdfmetrics.stringWidth(line,f,size)
    if e['align']=='center':tx+=(e['w']-sw)/2
    pdf.drawString(tx,H-e['y']-size*.81-i*size*e['leading'],line)
   sh=ps.shapes.add_textbox(Pt(e['x']*S),Pt(e['y']*S),Pt(e['w']*S),Pt(e['h']*S))
   tf=sh.text_frame;tf.clear();tf.margin_left=0;tf.margin_top=0;tf.margin_right=0;tf.margin_bottom=0;tf.word_wrap=False
   for i,line in enumerate(e['text'].split('\n')):
    p=tf.paragraphs[0] if i==0 else tf.add_paragraph();p.text=line;p.font.name='Inter' if e['font'] in ('Regular','Bold') else 'Inter '+e['font'];p.font.size=Pt(size*S);p.font.bold=e['font']=='Bold';p.font.color.rgb=rgb(e['color'])
    p.space_before=Pt(0);p.space_after=Pt(0);p.line_spacing=Pt(size*e['leading']*S)
    if e['align']=='center':p.alignment=PP_ALIGN.CENTER
  elif k=='image':
   p=ROOT/e['file'];pdf.drawImage(str(p),e['x'],H-e['y']-e['h'],e['w'],e['h'],mask='auto')
   ps.shapes.add_picture(str(p),Pt(e['x']*S),Pt(e['y']*S),Pt(e['w']*S),Pt(e['h']*S))
  elif k=='line':
   pdf.setStrokeColor(HexColor(e['color']));pdf.setLineWidth(e['width']);p=pdf.beginPath();p.moveTo(e['points'][0][0],H-e['points'][0][1])
   for x,y in e['points'][1:]:p.lineTo(x,H-y)
   if e['closed']:p.close()
   pdf.drawPath(p)
   pts=e['points']+([e['points'][0]] if e['closed'] else [])
   from pptx.enum.shapes import MSO_CONNECTOR
   for (x1,y1),(x2,y2) in zip(pts,pts[1:]):
    sh=ps.shapes.add_connector(MSO_CONNECTOR.STRAIGHT,Pt(x1*S),Pt(y1*S),Pt(x2*S),Pt(y2*S));sh.line.color.rgb=rgb(e['color']);sh.line.width=Pt(e['width']*S)
 pdf.showPage()
pdf.save();pr.save(str(ROOT/'Final_defense.pptx'))
(ROOT/'slides.json').write_text(json.dumps(SLIDES,ensure_ascii=False,indent=2),encoding='utf-8')

md=['# Речь для финальной защиты — 3 минуты','', 'Команда «Дебют в проде». Шесть слайдов; время включает короткие паузы.','']
doc=Document();normal=doc.styles['Normal'];normal.font.name='Inter';normal.font.size=DocPt(11);normal.paragraph_format.space_after=DocPt(8)
from docx.shared import RGBColor as DocRGB
for name in ['Title', 'Heading 1', 'Heading 2']:
 doc.styles[name].font.name='Inter';doc.styles[name].font.color.rgb=DocRGB.from_string('215B35')
doc.add_heading('Финальная защита · 3 минуты',0);doc.add_paragraph('Дебют в проде · Башкирская языковая школа')
for i,(timing,heading,body) in enumerate(SPEECH,1):
 md.extend([f'## {i}. {heading} · {timing}','',body,'']);doc.add_heading(f'{i}. {heading} · {timing}',2);doc.add_paragraph(body)
(ROOT/'Speech_3min.md').write_text('\n'.join(md),encoding='utf-8');doc.save(ROOT/'Speech_3min.docx')
with zipfile.ZipFile(ROOT/'Presentation_fonts.zip','w',zipfile.ZIP_DEFLATED) as z:
 for p in F.iterdir():
  if p.name!='Inter-variable.ttf':z.write(p,p.name)

readme='''# Финальная защита

- `Final_defense.pdf` — версия для показа, шрифт Inter встроен.
- `Final_defense.pptx` — редактируемая версия, речь находится в заметках слайдов.
- `Speech_3min.docx` и `Speech_3min.md` — речь с разбивкой по времени.
- `Presentation_fonts.zip` — Inter и лицензия; установите статические TTF перед редактированием PPTX на другом компьютере.
- `build_presentation.py` — воспроизводимая сборка из актуальных снимков интерфейса.

Презентация использует палитру приложения, без названия «Башперевод», серых нижних подписей и отдельного слайда про устойчивость. На слайдах нет вымышленных метрик внедрения. Снимки игрового интерфейса сделаны с подготовленным локальным профилем Дениса; числа в профиле иллюстрируют интерфейс. В речи отдельно обозначено, что игровая часть и серверные кабинеты ещё требуют сквозной интеграции.

Учтённые критерии: учебный путь и работа с реальными фразами — слайд 2; геймификация и рейтинги — слайд 3; культура — слайд 4; обработка ответов, фиксация переводов, данные и API — слайд 5; UX — реальные экраны приложения. Надёжность и документация кратко отражены в речи к слайду 5.

Фактические источники: исходное ТЗ `C:/Users/l_DK_l/Desktop/doc-1794511614.pdf`, `client/src`, `src/main-api/START-HERE.md`, `README.md`, `lms-client/README.md`, `expert-client/CSV-IMPORT.md`. Инструкции из исходного ТЗ использованы как контекст продукта и критериев оценки.
'''
(ROOT/'README.md').write_text(readme,encoding='utf-8')
words=sum(len(re.findall(r'\S+',s[2])) for s in SPEECH)
print(json.dumps({'slides':len(SLIDES),'speech_words':words,'files':['Final_defense.pdf','Final_defense.pptx','Speech_3min.docx','Speech_3min.md']},ensure_ascii=False))
