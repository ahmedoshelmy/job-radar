import { useState } from 'react'
import type { Application, Stage } from '../lib/api'
import { COLUMNS, daysInStage, dropTarget, groupByColumn } from '../lib/pipeline'

interface Props {
  applications: Application[]
  onMove: (id: string, stage: Stage) => Promise<void>
}

/** Kanban: drag a card to a column, or use its buttons (keyboard and touch friendly). Illegal drops are refused. */
export function Pipeline({ applications, onMove }: Props) {
  const [dragging, setDragging] = useState<Application | null>(null)
  const groups = groupByColumn(applications)
  const now = new Date()

  return (
    <section className="board" aria-label="Application pipeline">
      {COLUMNS.map((column) => {
        const target = dragging ? dropTarget(dragging, column.key) : null
        return (
          <div
            key={column.key}
            className={`column${target ? ' droppable' : ''}`}
            onDragOver={(e) => target && e.preventDefault()}
            onDrop={() => {
              if (dragging && target) void onMove(dragging.id, target)
              setDragging(null)
            }}
          >
            <h3>
              {column.title} <span className="muted">{groups[column.key].length}</span>
            </h3>
            {groups[column.key].map((a) => (
              <article key={a.id} className="card" draggable onDragStart={() => setDragging(a)} onDragEnd={() => setDragging(null)}>
                <a href={a.url} target="_blank" rel="noreferrer" className="title">
                  {a.title}
                </a>
                <div className="muted">{a.company}</div>
                <div className="muted small">
                  {a.stage} · {daysInStage(a, now)} d · {a.history.length} step{a.history.length === 1 ? '' : 's'}
                </div>
                {a.allowedNext.length > 0 && (
                  <div className="moves">
                    {a.allowedNext.map((s) => (
                      <button key={s} className="ghost small" onClick={() => void onMove(a.id, s)}>
                        → {s}
                      </button>
                    ))}
                  </div>
                )}
              </article>
            ))}
          </div>
        )
      })}
    </section>
  )
}
