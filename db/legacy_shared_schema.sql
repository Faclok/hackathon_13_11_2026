CREATE TABLE app_users (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    username text UNIQUE,
    password_hash text,
    display_name text NOT NULL,
    telegram_id bigint UNIQUE,
    lang_pair text NOT NULL DEFAULT 'ba-ru' CHECK (lang_pair = 'ba-ru'),
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE sessions (
    token_hash text PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
    expires_at timestamptz NOT NULL
);
CREATE INDEX sessions_user_idx ON sessions(user_id);

CREATE TABLE source_items (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    dataset text NOT NULL,
    original_id text NOT NULL,
    title text NOT NULL,
    source_url text,
    attribution text NOT NULL,
    license text NOT NULL,
    UNIQUE(dataset, original_id)
);
CREATE TABLE fragments (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    source_id uuid NOT NULL REFERENCES source_items(id),
    text_ba text NOT NULL,
    quality_status text NOT NULL DEFAULT 'demo' CHECK (quality_status IN ('demo','reviewed'))
);
CREATE TABLE translation_candidates (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    fragment_id uuid NOT NULL REFERENCES fragments(id),
    lang_pair text NOT NULL DEFAULT 'ba-ru',
    text text NOT NULL,
    origin text NOT NULL DEFAULT 'demo',
    UNIQUE(fragment_id, lang_pair)
);
CREATE TABLE daily_tasks (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES app_users(id),
    fragment_id uuid NOT NULL REFERENCES fragments(id),
    candidate_id uuid NOT NULL REFERENCES translation_candidates(id),
    lang_pair text NOT NULL,
    task_date date NOT NULL,
    UNIQUE(user_id, lang_pair, task_date)
);
CREATE TABLE submissions (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    task_id uuid NOT NULL UNIQUE REFERENCES daily_tasks(id),
    user_id uuid NOT NULL REFERENCES app_users(id),
    fragment_id uuid NOT NULL REFERENCES fragments(id),
    lang_pair text NOT NULL,
    action text NOT NULL CHECK (action IN ('confirm','correct')),
    answer_text text NOT NULL,
    updated_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX submissions_fragment_idx ON submissions(fragment_id,lang_pair);
CREATE TABLE consensus_decisions (
    fragment_id uuid NOT NULL REFERENCES fragments(id),
    lang_pair text NOT NULL,
    translation text,
    status text NOT NULL CHECK (status IN ('collecting','disputed','confirmed')),
    confirmations integer NOT NULL DEFAULT 0,
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(fragment_id,lang_pair)
);
CREATE TABLE point_events (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id uuid NOT NULL REFERENCES app_users(id),
    submission_id uuid NOT NULL UNIQUE REFERENCES submissions(id),
    points integer NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE bot_states (
    telegram_id bigint PRIMARY KEY,
    state text NOT NULL DEFAULT 'idle',
    task_id uuid,
    updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE lms_courses (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    title text NOT NULL,
    description text NOT NULL
);
CREATE TABLE lms_lessons (
    id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    course_id integer NOT NULL REFERENCES lms_courses(id),
    position integer NOT NULL,
    title text NOT NULL,
    body text NOT NULL,
    UNIQUE(course_id,position)
);
CREATE TABLE lms_progress (
    user_id uuid NOT NULL REFERENCES app_users(id),
    lesson_id integer NOT NULL REFERENCES lms_lessons(id),
    completed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(user_id,lesson_id)
);

-- These phrases are demo content only; review language quality before public use.
INSERT INTO source_items(dataset,original_id,title,source_url,attribution,license)
VALUES ('demo','starter','Демонстрационные фразы',NULL,'Команда проекта','Авторский демонстрационный материал');
WITH src AS (SELECT id FROM source_items WHERE dataset='demo' AND original_id='starter'),
phrases(text_ba,text_ru) AS (VALUES
  ('Һаумыһығыҙ!', 'Здравствуйте!'),
  ('Рәхмәт!', 'Спасибо!'),
  ('Хәйерле иртә!', 'Доброе утро!'),
  ('Һау булығыҙ!', 'До свидания!'),
  ('Хәлдәр нисек?', 'Как дела?'),
  ('Мин башҡорт телен өйрәнәм.', 'Я изучаю башкирский язык.')
), inserted AS (
  INSERT INTO fragments(source_id,text_ba)
  SELECT src.id, phrases.text_ba FROM src CROSS JOIN phrases
  RETURNING id,text_ba
)
INSERT INTO translation_candidates(fragment_id,text)
SELECT inserted.id, phrases.text_ru FROM inserted JOIN phrases USING(text_ba);

INSERT INTO lms_courses(title,description) VALUES
('Башкирский язык: первые шаги','Короткий вводный курс к ежедневной практике перевода.');
INSERT INTO lms_lessons(course_id,position,title,body) VALUES
(1,1,'Приветствие','Һаумыһығыҙ — «Здравствуйте». Повторите фразу и перейдите к заданию дня.'),
(1,2,'Благодарность','Рәхмәт — «Спасибо». Попробуйте узнать эту фразу в задании.'),
(1,3,'Практика','Проверяйте предложенный перевод и исправляйте его, если нужно. Ответы нескольких людей помогают согласовать перевод.');
