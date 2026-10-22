;;; DeleteAllHatches.lsp
;;; ---------------------------------------------------------------------------
;;; Command: DELHATCH
;;;
;;; Deletes every hatch in the drawing:
;;;   - in Model Space and all Paper Space layouts
;;;   - inside every block definition (including nested and dynamic/anonymous
;;;     blocks), so the hatches disappear from all block references
;;;
;;; Xrefs and xref-dependent blocks are skipped (they can't be edited here).
;;; Locked layers are temporarily unlocked so their hatches can be removed,
;;; then re-locked. The whole operation is a single UNDO step.
;;;
;;; Usage: APPLOAD this file (or drag it into the drawing), then type DELHATCH.
;;; ---------------------------------------------------------------------------

(vl-load-com)

(defun c:DELHATCH (/ acad doc layers locked count failed)
  (setq acad   (vlax-get-acad-object)
        doc    (vla-get-ActiveDocument acad)
        layers (vla-get-Layers doc)
        locked '()
        count  0
        failed 0)

  (vla-StartUndoMark doc)

  ;; Unlock any locked layers, remembering which ones to re-lock.
  (vlax-for lay layers
    (if (= (vla-get-Lock lay) :vlax-true)
      (progn
        (setq locked (cons lay locked))
        (vla-put-Lock lay :vlax-false))))

  ;; Walk every block definition. Model Space and Paper Space layouts are
  ;; themselves entries in the Blocks collection, so this covers objects
  ;; both in and out of blocks.
  (vlax-for blk (vla-get-Blocks doc)
    (if (and (= (vla-get-IsXref blk) :vlax-false)
             (not (wcmatch (vla-get-Name blk) "*|*")))
      (vlax-for obj blk
        (if (= (vla-get-ObjectName obj) "AcDbHatch")
          (if (vl-catch-all-error-p (vl-catch-all-apply 'vla-Delete (list obj)))
            (setq failed (1+ failed))
            (setq count (1+ count)))))))

  ;; Restore layer locks.
  (foreach lay locked
    (vla-put-Lock lay :vlax-true))

  (vla-Regen doc acAllViewports)
  (vla-EndUndoMark doc)

  (princ (strcat "\nDeleted " (itoa count) " hatch(es)."))
  (if (> failed 0)
    (princ (strcat "\nCould not delete " (itoa failed) " hatch(es).")))
  (princ))

(princ "\nDeleteAllHatches loaded. Type DELHATCH to run.")
(princ)
