;;; MergeHatch.lsp
;;; Command: MERGEHATCH
;;;
;;; Combines a bunch of separate hatches into ONE hatch object.
;;; - The new hatch takes the pattern, scale, angle, layer, color, etc. of
;;;   the FIRST hatch you pick (the rest just contribute their boundaries).
;;; - The result is non-associative (it no longer follows its old boundaries).
;;; - Hatches on a different plane/elevation than the first one are skipped.
;;; - The original hatches are deleted. One U undoes the whole thing.
;;; Note: if hatch areas overlap, the overlap may show as an island (hole),
;;;       same as the hatch's normal island detection.

(vl-load-com)

;; Split hatch DXF data into (head loops tail):
;; head  = everything up to and including the 91 (loop count)
;; loops = all boundary loop data
;; tail  = from the 75 (hatch style) onward: pattern, seed points, gradient...
(defun mh:split (ed / head loops tail mode)
  (setq mode 0)
  (foreach a ed
    (cond
      ((= mode 0) (setq head (cons a head)) (if (= (car a) 91) (setq mode 1)))
      ((and (= mode 1) (= (car a) 75)) (setq mode 2 tail (cons a tail)))
      ((= mode 1) (setq loops (cons a loops)))
      (t (setq tail (cons a tail)))
    )
  )
  (list (reverse head) (reverse loops) (reverse tail))
)

;; Remove links to source boundary objects (makes the loops non-associative).
;; A 97 followed by 330s is a source-object count; a 97 inside a spline edge
;; is a fit-point count followed by 11s and is left alone.
(defun mh:cleanloops (lst / a out)
  (while lst
    (setq a (car lst) lst (cdr lst))
    (cond
      ((and (= (car a) 97) (= (caar lst) 330))
       (setq out (cons '(97 . 0) out))
       (while (= (caar lst) 330) (setq lst (cdr lst)))
      )
      ((= (car a) 330))
      (t (setq out (cons a out)))
    )
  )
  (reverse out)
)

(defun mh:plane (ed)
  (list (cdr (assoc 210 ed)) (caddr (cdr (assoc 10 ed))))
)

(defun c:MERGEHATCH (/ doc ss i en ed parts first loops nloops used skipped head new)
  (setq doc (vla-get-activedocument (vlax-get-acad-object)))
  (princ "\nSelect hatches to merge (first one picked sets the pattern): ")
  (if (and (setq ss (ssget '((0 . "HATCH")))) (> (sslength ss) 1))
    (progn
      (vla-startundomark doc)
      (setq i 0 nloops 0 skipped 0)
      (repeat (sslength ss)
        (setq en (ssname ss i)
              ed (entget en)
              i  (1+ i))
        (if (or (null first) (equal (mh:plane ed) (mh:plane first) 1e-8))
          (progn
            (if (null first) (setq first ed))
            (setq parts  (mh:split ed)
                  loops  (append loops (mh:cleanloops (cadr parts)))
                  nloops (+ nloops (cdr (assoc 91 ed)))
                  used   (cons en used))
          )
          (setq skipped (1+ skipped))
        )
      )
      ;; Build the new hatch from the first one's settings + all loops
      (setq parts (mh:split first)
            head  (vl-remove-if
                    '(lambda (a) (member (car a) '(-1 5 102 330 360)))
                    (car parts))
            head  (subst '(71 . 0) (assoc 71 head) head)
            head  (subst (cons 91 nloops) (assoc 91 head) head)
            new   (append head loops (caddr parts)))
      (if (and (> (length used) 1) (entmake new))
        (progn
          (foreach e used (entdel e))
          (princ (strcat "\nMerged " (itoa (length used)) " hatches into one."))
        )
        (princ "\nCould not merge - nothing was changed.")
      )
      (if (> skipped 0)
        (princ (strcat "\n" (itoa skipped) " hatch(es) skipped (different plane/elevation)."))
      )
      (vla-endundomark doc)
    )
    (princ "\nSelect at least 2 hatches.")
  )
  (princ)
)

(princ "\nMergeHatch loaded. Type MERGEHATCH to run.")
(princ)
