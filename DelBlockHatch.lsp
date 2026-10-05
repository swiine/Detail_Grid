;;; DelBlockHatch.lsp
;;; Deletes every hatch inside every block definition in the current drawing.
;;;
;;; Usage:
;;;   1. APPLOAD this file (or drag it into the drawing).
;;;   2. Type DELBLOCKHATCH at the command line.
;;;
;;; Notes:
;;;   - Works on block definitions, so every insert of a block is cleaned at once,
;;;     including nested blocks and anonymous (dynamic block) definitions.
;;;   - Hatches drawn directly in model space or paper space are left alone.
;;;   - Xrefs and xref-dependent blocks are skipped (edit the source drawing instead).
;;;   - Locked layers are unlocked temporarily and relocked afterwards.
;;;   - The whole operation is a single UNDO step.

(vl-load-com)

(defun c:DelBlockHatch ( / *error* acDoc locked hatches count blkCount)

  (defun *error* (msg)
    (foreach lay locked (vl-catch-all-apply 'vla-put-Lock (list lay :vlax-true)))
    (if acDoc (vla-EndUndoMark acDoc))
    (if (not (wcmatch (strcase msg) "*CANCEL*,*QUIT*,*BREAK*"))
      (princ (strcat "\nError: " msg)))
    (princ))

  (setq acDoc    (vla-get-ActiveDocument (vlax-get-acad-object))
        count    0
        blkCount 0)
  (vla-StartUndoMark acDoc)

  ;; Temporarily unlock locked layers so hatches on them can be erased.
  (vlax-for lay (vla-get-Layers acDoc)
    (if (= :vlax-true (vla-get-Lock lay))
      (progn
        (setq locked (cons lay locked))
        (vla-put-Lock lay :vlax-false))))

  (vlax-for blk (vla-get-Blocks acDoc)
    (if (and (= :vlax-false (vla-get-IsLayout blk))
             (= :vlax-false (vla-get-IsXRef blk))
             (not (wcmatch (vla-get-Name blk) "*|*")))
      (progn
        ;; Collect first, then delete, so the collection isn't modified while iterating.
        (setq hatches nil)
        (vlax-for obj blk
          (if (= "AcDbHatch" (vla-get-ObjectName obj))
            (setq hatches (cons obj hatches))))
        (if hatches
          (progn
            (setq blkCount (1+ blkCount))
            (foreach h hatches
              (if (not (vl-catch-all-error-p (vl-catch-all-apply 'vla-Delete (list h))))
                (setq count (1+ count)))))))))

  (foreach lay locked (vla-put-Lock lay :vlax-true))
  (setq locked nil)

  (vla-Regen acDoc acAllViewports)
  (vla-EndUndoMark acDoc)
  (princ (strcat "\nDeleted " (itoa count) " hatch(es) from "
                 (itoa blkCount) " block definition(s)."))
  (princ))

(princ "\nDelBlockHatch loaded. Type DELBLOCKHATCH to remove all hatches inside blocks.")
(princ)
