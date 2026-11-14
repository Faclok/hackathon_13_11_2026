import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { getLessons } from "../api";
import { units } from "../mocks/data";
import { useProgressStore } from "../store";
import type { Lesson } from "../types";
import { Icon, PageTitle, ProgressBar } from "../components/ui";

export default function Learn() {
  const [lessons, setLessons] = useState<Lesson[]>([]);
  const completed = useProgressStore((s) => s.completedLessons);
  useEffect(() => {
    getLessons().then(setLessons);
  }, []);
  return (
    <>
      <PageTitle subtitle="От первых слов к уверенной речи">Уроки</PageTitle>
      <section className="surface learning-progress">
        <div>
          <strong>Твой путь</strong>
          <span>
            {completed.length} / {lessons.length || 15} уроков
          </span>
        </div>
        <ProgressBar value={completed.length} max={lessons.length || 15} />
      </section>
      <div className="unit-list">
        {units.map((unit, unitIndex) => (
          <section className="surface unit-card" key={unit.id}>
            <div className="unit-heading">
              <span className="unit-emoji">{unit.icon}</span>
              <div>
                <span className="eyebrow-label">РАЗДЕЛ {unitIndex + 1}</span>
                <h2>{unit.title}</h2>
                <p>{unit.description}</p>
              </div>
            </div>
            <div className="lesson-path">
              {lessons
                .filter((l) => l.unitId === unit.id)
                .map((lesson) => {
                  const index = lessons.indexOf(lesson);
                  const done = completed.includes(lesson.id);
                  const locked =
                    index > 0 && !completed.includes(lessons[index - 1].id);
                  return (
                    <div className="lesson-node" key={lesson.id}>
                      {locked ? (
                        <button
                          className="lesson-circle locked"
                          disabled
                          aria-label={`${lesson.title}: сначала пройди предыдущий урок`}
                        >
                          <Icon name="lock" />
                        </button>
                      ) : (
                        <Link
                          to={`/lesson/${lesson.id}`}
                          className={`lesson-circle ${done ? "done" : "available"}`}
                          aria-label={lesson.title}
                        >
                          {done ? <Icon name="check" /> : (index % 3) + 1}
                        </Link>
                      )}
                      <span>Урок {(index % 3) + 1}</span>
                    </div>
                  );
                })}
            </div>
          </section>
        ))}
      </div>
    </>
  );
}
