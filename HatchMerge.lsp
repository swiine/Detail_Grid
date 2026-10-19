;;; HatchMerge.lsp
;;; Command: HATCHMERGE
;;; Merges all selected hatches into a single hatch object.
;;; The first hatch selected supplies the pattern, scale, angle, layer,
;;; colour, etc.; the boundaries of all other hatches are added to it.
;;; The result is non-associative. Hatches on a different plane
;;; (elevation/normal) from the first one are skipped.

(vl-load-com)

;; Split hatch DXF data into (head loops tail):
;;   head  = everything before the 91 (loop count) group
;;   loops = boundary loop data
;;   tail  = everything from the 75 (hatch style) group onwards
(defun HatchMerge:split (ed / head loops)
  (while (/= (caar ed) 91)
    (setq head (cons (car ed) head) ed (cdr ed)))
  (setq ed (cdr ed))
  (while (/= (caar ed) 75)
    (setq loops (cons (car ed) loops) ed (cdr ed)))
  (list (reverse head) (reverse loops) ed))

;; Remove links to boundary objects (97 count + 330 handles) from loop data.
;; A 97 followed by 11s is spline fit data and is left alone.
(defun HatchMerge:cleanLoops (lst / out itm)
  (while lst
    (setq itm (car lst) lst (cdr lst))
    (if (and (= (car itm) 97) (> (cdr itm) 0) (= (caar lst) 330))
      (progn
        (setq out (cons '(97 . 0) out))
        (repeat (cdr itm) (setq lst (cdr lst))))
      (setq out (cons itm out))))
  (reverse out))

;; Strip entity name, handle, owner and reactor groups; make non-associative
(defun HatchMerge:cleanHead (lst / out skip)
  (foreach itm lst
    (cond
      ((and (= (car itm) 102) (= (cdr itm) "{ACAD_REACTORS")) (setq skip T))
      ((and skip (= (car itm) 102)) (setq skip nil))
      ((or skip (member (car itm) '(-1 5 330 360))))
      ((= (car itm) 71) (setq out (cons '(71 . 0) out)))
      (T (setq out (cons itm out)))))
  (reverse out))

(defun c:HATCHMERGE (/ *error* doc ss base baseEd parts elev norm loops total
                       i ent ed p skipped newEnt)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))

  (defun *error* (msg)
    (vla-EndUndoMark doc)
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*EXIT*"))
      (princ (strcat "\nError: " msg)))
    (princ))

  (princ "\nSelect hatches to merge: ")
  (if (and (setq ss (ssget "_:L" '((0 . "HATCH"))))
           (> (sslength ss) 1))
    (progn
      (vla-StartUndoMark doc)
      (setq base   (ssname ss 0)
            baseEd (entget base)
            parts  (HatchMerge:split baseEd)
            elev   (caddr (cdr (assoc 10 baseEd)))
            norm   (cdr (assoc 210 baseEd))
            loops  (HatchMerge:cleanLoops (cadr parts))
            total  (cdr (assoc 91 baseEd))
            skipped 0
            i      1)

      ;; Collect the boundary loops of every other hatch
      (repeat (1- (sslength ss))
        (setq ent (ssname ss i)
              ed  (entget ent))
        (if (and (equal (cdr (assoc 210 ed)) norm 1e-8)
                 (equal (caddr (cdr (assoc 10 ed))) elev 1e-8))
          (setq p     (HatchMerge:split ed)
                loops (append loops (HatchMerge:cleanLoops (cadr p)))
                total (+ total (cdr (assoc 91 ed))))
          (progn
            (ssdel ent ss)
            (setq i (1- i) skipped (1+ skipped))))
        (setq i (1+ i)))

      ;; Build the merged hatch and delete the originals
      (if (and (> (sslength ss) 1)
               (setq newEnt
                 (entmake (append (HatchMerge:cleanHead (car parts))
                                  (list (cons 91 total))
                                  loops
                                  (caddr parts)))))
        (progn
          (setq i 0)
          (repeat (sslength ss)
            (entdel (ssname ss i))
            (setq i (1+ i)))
          (princ (strcat "\n" (itoa (sslength ss)) " hatches merged into one.")))
        (princ "\nCould not merge the selected hatches."))

      (if (> skipped 0)
        (princ (strcat "\n" (itoa skipped)
                       " hatch(es) skipped (different elevation or plane).")))
      (vla-EndUndoMark doc))
    (princ "\nSelect at least 2 hatches."))
  (princ))

(princ "\nHatchMerge loaded. Type HATCHMERGE to run.")
(princ)
