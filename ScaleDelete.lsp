;;; ScaleDelete.lsp
;;;
;;; Deletes every annotation scale in the drawing's scale list that nothing uses.
;;;
;;; A scale counts as used when any of these is true:
;;;   - it is the 1:1 scale (AutoCAD needs it)
;;;   - it is the current annotation scale (CANNOSCALE)
;;;   - an annotative object, attribute or style carries it as an object scale
;;;   - an entity (e.g. a viewport) points to it directly
;;;   - a paper space viewport is set to the same ratio
;;;
;;; Commands:
;;;   SCALEDELETE       list the unused scales, ask for confirmation, delete them
;;;   LISTUNUSEDSCALES  only list the unused scales, change nothing
;;;
;;; Load with APPLOAD (or drag the file into the drawing), then run a command.

(vl-load-com)

;; All SCALE objects in ACAD_SCALELIST as a list of (name . ename).
(defun dus:scale-list (/ dict ent result)
  (if (setq dict (cdr (assoc -1 (dictsearch (namedobjdict) "ACAD_SCALELIST"))))
    (progn
      (setq ent (dictnext dict T))
      (while ent
        (setq result (cons (cons (cdr (assoc 300 ent)) (cdr (assoc -1 ent))) result))
        (setq ent (dictnext dict))
      )
    )
  )
  (reverse result)
)

;; Every 340 (hard pointer) value in an object's data.
(defun dus:pointers (ename / result)
  (foreach pair (entget ename)
    (if (= (car pair) 340)
      (setq result (cons (cdr pair) result))
    )
  )
  result
)

;; Pointers found anywhere below a dictionary, recursing into sub-dictionaries.
;; Entries are gathered before recursing so nested dictnext calls can't
;; disturb the outer iteration.
(defun dus:dict-pointers (dict / ent entries result)
  (setq ent (dictnext dict T))
  (while ent
    (setq entries (cons ent entries))
    (setq ent (dictnext dict))
  )
  (foreach ent entries
    (setq result
      (append
        (if (= (cdr (assoc 0 ent)) "DICTIONARY")
          (dus:dict-pointers (cdr (assoc -1 ent)))
          (dus:pointers (cdr (assoc -1 ent)))
        )
        result
      )
    )
  )
  result
)

;; Scales an object references: direct 340 pointers plus the object context
;; data (annotative scales) stored in its extension dictionary.
(defun dus:object-refs (ename / xdict ctx)
  (append
    (dus:pointers ename)
    (if (and (setq xdict (cdr (assoc 360 (entget ename))))
             (setq ctx (dictsearch xdict "AcDbContextDataManager"))
        )
      (dus:dict-pointers (cdr (assoc -1 ctx)))
    )
  )
)

;; Collect every referenced ename and every viewport ratio in the drawing.
;; Returns (refs . ratios).
(defun dus:collect-usage (doc / refs ratios rec mlstyles)
  ;; Entities in all blocks, model space and layouts
  (vlax-for blk (vla-get-Blocks doc)
    (vlax-for obj blk
      (setq refs (append (dus:object-refs (vlax-vla-object->ename obj)) refs))
      (if (and (= (vla-get-ObjectName obj) "AcDbBlockReference")
               (= (vla-get-HasAttributes obj) :vlax-true)
          )
        (foreach att (vlax-invoke obj 'GetAttributes)
          (setq refs (append (dus:object-refs (vlax-vla-object->ename att)) refs))
        )
      )
      (if (= (vla-get-ObjectName obj) "AcDbViewport")
        (setq ratios (cons (vla-get-CustomScale obj) ratios))
      )
    )
  )
  ;; Annotative text and dimension styles
  (foreach table '("STYLE" "DIMSTYLE")
    (setq rec (tblnext table T))
    (while rec
      (setq refs (append (dus:object-refs (tblobjname table (cdr (assoc 2 rec)))) refs))
      (setq rec (tblnext table))
    )
  )
  ;; Multileader styles
  (if (setq mlstyles (dictsearch (namedobjdict) "ACAD_MLEADERSTYLE"))
    (setq refs (append (dus:dict-pointers (cdr (assoc -1 mlstyles))) refs))
  )
  (cons refs ratios)
)

;; Paper units / drawing units of a SCALE object.
(defun dus:ratio (ename / data drawing)
  (setq data (entget ename)
        drawing (cdr (assoc 141 data))
  )
  (if (and drawing (/= drawing 0.0))
    (/ (cdr (assoc 140 data)) drawing)
  )
)

(defun dus:ratio-used-p (ratio ratios / found)
  (if ratio
    (foreach r ratios
      (if (equal r ratio (* 1e-6 (max (abs r) (abs ratio))))
        (setq found T)
      )
    )
  )
  found
)

;; List of (name . ename) for scales nothing uses.
(defun dus:unused-scales (/ doc usage refs ratios current result)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object))
        usage (dus:collect-usage doc)
        refs (car usage)
        ratios (cdr usage)
        current (strcase (getvar "CANNOSCALE"))
  )
  (foreach scale (dus:scale-list)
    (if (not (or (= (cdr (assoc 290 (entget (cdr scale)))) 1) ; 1:1 unit scale
                 (= (strcase (car scale)) current)
                 (member (cdr scale) refs)
                 (dus:ratio-used-p (dus:ratio (cdr scale)) ratios)
             )
        )
      (setq result (cons scale result))
    )
  )
  (reverse result)
)

(defun dus:print-list (scales)
  (foreach scale scales
    (princ (strcat "\n  " (car scale)))
  )
)

(defun c:LISTUNUSEDSCALES (/ unused)
  (setq unused (dus:unused-scales))
  (if unused
    (progn
      (princ (strcat "\n" (itoa (length unused)) " unused scale(s):"))
      (dus:print-list unused)
    )
    (princ "\nNo unused scales found.")
  )
  (princ)
)

(defun c:SCALEDELETE (/ unused doc answer deleted failed)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object))
        unused (dus:unused-scales)
        deleted 0
  )
  (if unused
    (progn
      (princ (strcat "\n" (itoa (length unused)) " unused scale(s):"))
      (dus:print-list unused)
      (initget "Yes No")
      (setq answer (getkword "\nDelete these scales? [Yes/No] <No>: "))
      (if (= answer "Yes")
        (progn
          (vla-StartUndoMark doc)
          (foreach scale unused
            (if (vl-catch-all-error-p
                  (vl-catch-all-apply 'vla-Delete (list (vlax-ename->vla-object (cdr scale))))
                )
              (setq failed (cons (car scale) failed))
              (setq deleted (1+ deleted))
            )
          )
          (vla-EndUndoMark doc)
          (princ (strcat "\nDeleted " (itoa deleted) " scale(s)."))
          (if failed
            (progn
              (princ "\nCould not delete:")
              (foreach name (reverse failed) (princ (strcat "\n  " name)))
            )
          )
        )
        (princ "\nNothing deleted.")
      )
    )
    (princ "\nNo unused scales found.")
  )
  (princ)
)

(princ "\nScaleDelete loaded. Commands: SCALEDELETE, LISTUNUSEDSCALES")
(princ)
