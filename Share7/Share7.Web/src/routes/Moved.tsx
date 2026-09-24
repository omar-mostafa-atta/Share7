import { motion } from 'motion/react'
import { GraduationCap, Building2, Milestone } from 'lucide-react'
import { Link } from 'react-router-dom'
import { Note, PageTitle } from '../components/ui/bits'
import { listVariants, riseVariants } from '../components/ui/motion'

// ===========================================================================
// Content authoring moved
//
// What an old bookmark answers with. /curriculum, /quality, /targets and
// /content were this console's authoring surface until cutover (plan P6);
// authoring now happens in the Content Studio, where every change is reviewed
// by a second person before a Lead releases it.
//
// It is a page rather than a redirect on purpose. A silent bounce to the
// dashboard is indistinguishable from a bug, and the admin who followed a
// three-year-old link would go looking for the tree in the sidebar and find
// nothing. This is the console's half of the 410 the old API routes answer.
//
// It does not link to the Studio. Admins are not Studio members — approved
// default #4 keeps even SuperAdmins out of it — so a button here would be a
// door that will not open for whoever is reading. What it links to instead is
// the measurement that stayed.
//
// The motion is arranged so the stylesheet's blank-page guard covers it: the
// two containers animate nothing but timing, and everything that starts at
// opacity 0 is a direct child of `.s7-stack`, which console.css holds at its
// resting state until the document has been seen. A page whose whole job is to
// be read must not depend on an animation having run. See lib/visibility.ts.
// ===========================================================================

export function Moved() {
  return (
    <motion.div variants={listVariants} initial="hidden" animate="visible">
      <PageTitle
        icon={<Milestone size={17} />}
        title="Content authoring moved"
        subtitle="The curriculum, lesson questions, answer quality and learning skills are now built in the Content Studio."
      />

      {/* 30rem, not a `ch` count: `ch` resolves against this container's inherited 16px while
          everything inside it sets its own smaller size, so a measure written in `ch` here came
          out at 96 characters a line. This is ~70 for the paragraph and the note alike. */}
      <motion.div variants={listVariants} className="s7-stack" style={{ maxWidth: '30rem' }}>
        <motion.p
          variants={riseVariants}
          className="s7-muted"
          style={{ fontSize: '0.88rem', lineHeight: 1.65 }}
        >
          The content team works there, on their own accounts, and nothing they write reaches a
          student until somebody else has approved it and a Lead has released it. That review is the
          whole reason the pages left this console: while both existed, there were two ways to
          publish and only one of them was reviewed.
        </motion.p>

        <motion.div variants={riseVariants}>
          <Note>
            The old addresses still answer — this page, and a plain refusal on the API — rather than
            disappearing. Anything still calling them is told where the work went instead of getting
            a 404 it cannot tell apart from a typo.
          </Note>
        </motion.div>

        <motion.div variants={riseVariants}>
          <span className="s7-label">Still here</span>
          <ul className="s7-moved-list">
            <li>
              <Link to="/exams">
                <GraduationCap size={15} aria-hidden />
                Examinations
              </Link>
              <span>
                Blueprints, coverage thresholds and calibration. A paper is still designed here; a
                Lead freezes it in the Studio.
              </span>
            </li>
            <li>
              <Link to="/organizations">
                <Building2 size={15} aria-hidden />
                Organizations
              </Link>
              <span>
                Schools, cohorts, rosters and assignments — who learns what, rather than what there
                is to learn.
              </span>
            </li>
          </ul>
        </motion.div>
      </motion.div>
    </motion.div>
  )
}
