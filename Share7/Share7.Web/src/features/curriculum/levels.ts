// ===========================================================================
// Curriculum level definitions
//
// Ported from the `levels` map in wwwroot/js/curriculum.js. One table drives
// all five columns, so a level is added by adding a row here rather than by
// writing another column component.
// ===========================================================================

export type Level = 'grade' | 'term' | 'subject' | 'chapter' | 'lesson'

/** Root to leaf. Used for "clear everything below this" and for rendering order. */
export const LEVEL_ORDER: readonly Level[] = ['grade', 'term', 'subject', 'chapter', 'lesson']

export const LEVEL_LABELS: Record<Level, string> = {
  grade: 'Grades',
  term: 'Terms',
  subject: 'Subjects',
  chapter: 'Chapters',
  lesson: 'Lessons',
}

interface LevelConfig {
  parent: Level
  next: Level | null

  /** Children of the given parent id. */
  list: (parentId: string) => string

  /** POST target that creates a child under the given parent id. */
  create: (parentId: string) => string

  /** DELETE target for a node of this level. */
  remove: (id: string) => string
}

/**
 * Grades are absent on purpose, and it is not an omission: AdminCurriculumController exposes
 * AddTerm/AddSubject/AddChapter/AddLesson and the four matching deletes, but there is no
 * create-grade or delete-grade endpoint anywhere in the API. The ladder is seeded from
 * Domain/Constants/GradeIds.cs. So the grades column is read-only, and a level missing from this
 * table is what makes it so.
 */
export const LEVELS: Record<Exclude<Level, 'grade'>, LevelConfig> = {
  term: {
    parent: 'grade',
    next: 'subject',
    list: (id) => `/api/terms?gradeId=${id}`,
    create: (id) => `/api/admin/grades/${id}/terms`,
    remove: (id) => `/api/admin/terms/${id}`,
  },
  subject: {
    parent: 'term',
    next: 'chapter',
    list: (id) => `/api/subjects?termId=${id}`,
    create: (id) => `/api/admin/terms/${id}/subjects`,
    remove: (id) => `/api/admin/subjects/${id}`,
  },
  chapter: {
    parent: 'subject',
    next: 'lesson',
    list: (id) => `/api/chapters?subjectId=${id}`,
    create: (id) => `/api/admin/subjects/${id}/chapters`,
    remove: (id) => `/api/admin/chapters/${id}`,
  },
  lesson: {
    parent: 'chapter',
    next: null,
    list: (id) => `/api/lessons?chapterId=${id}`,
    create: (id) => `/api/admin/chapters/${id}/lessons`,
    remove: (id) => `/api/admin/lessons/${id}`,
  },
}

export const isEditable = (level: Level): level is Exclude<Level, 'grade'> => level !== 'grade'

/** Levels below the given one, root-to-leaf. */
export function levelsBelow(level: Level): Level[] {
  return LEVEL_ORDER.slice(LEVEL_ORDER.indexOf(level) + 1)
}
